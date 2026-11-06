using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Dtr;

/// <summary>
/// Adaptive Questionnaires (SDC adaptive forms as DTR 2.2.0 profiles them): the plan hands out a questionnaire's
/// items a set at a time, in the QuestionnaireResponse's contained Questionnaire, choosing the next set from the
/// answers so far. A set runs from the first item not yet handed out up to the first one that later items depend
/// on (an enableWhen question), skipping items whose enableWhen the answers do not satisfy. When nothing is left
/// the response is completed.
/// </summary>
public sealed class AdaptiveForms(DocumentationQuestionnaires questionnaires)
{
    public const string AdaptiveExtension = "http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-questionnaireAdaptive";
    public const string AdaptiveQuestionnaireProfile = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-questionnaire-adapt";
    public const string AdaptiveResponseProfile = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-questionnaireresponse-adapt";
    public const string NextQuestionOutputProfile = "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/dtr-next-question-output-parameters";
    public const string ContainedQuestionnaireId = "contained-questionnaire";

    /// <summary>
    /// The Questionnaire handed out for an adaptive form: the source's identity without its items, saying where
    /// the questions come from (the $next-question endpoint at <paramref name="serverBase"/>).
    /// </summary>
    public static Questionnaire Shell(DocumentationForm form, string serverBase)
    {
        var shell = (Questionnaire)form.Source.DeepCopy();
        shell.Id = form.Source.Id + DocumentationQuestionnaires.AdaptiveSuffix;
        shell.Url = form.Url;
        shell.Meta = new Meta { Profile = [AdaptiveQuestionnaireProfile] };
        shell.DerivedFrom = [form.SourceCanonical];
        shell.Item.Clear();
        shell.Extension.RemoveAll(e => e.Url == AdaptiveExtension);
        shell.Extension.Add(new Extension(AdaptiveExtension, new FhirUrl(serverBase + "/Questionnaire/$next-question")));
        return shell;
    }

    /// <summary>The contained Questionnaire a started adaptive response carries: the shell with the first set of items.</summary>
    public static Questionnaire Started(DocumentationForm form, string serverBase)
    {
        var contained = Shell(form, serverBase);
        contained.Id = ContainedQuestionnaireId;
        contained.Item.AddRange(NextSet(form.Source, new HashSet<string>(), new Dictionary<string, List<DataType>>())
            .Select(i => (Questionnaire.ItemComponent)i.DeepCopy()));
        return contained;
    }

    /// <summary>
    /// $next-question: appends the next set of items to the response's contained Questionnaire, or completes the
    /// response when there is none.
    /// </summary>
    /// <exception cref="RequestRejected">The input is not an in-progress response to a form the plan holds.</exception>
    public Parameters NextQuestion(Parameters input)
    {
        var response = input.Parameter.FirstOrDefault(p => p.Name == "questionnaire-response")?.Resource as QuestionnaireResponse
                       ?? throw new RequestRejected("The questionnaire-response parameter is required: the QuestionnaireResponse so far, as a resource.");
        var contained = response.Contained.OfType<Questionnaire>().FirstOrDefault()
                        ?? throw new RequestRejected("The QuestionnaireResponse must contain the adaptive Questionnaire it answers (QuestionnaireResponse.contained).");
        var derivedFrom = contained.DerivedFrom.FirstOrDefault()
                          ?? throw new RequestRejected("The contained Questionnaire must say which of the plan's questionnaires it derives from (Questionnaire.derivedFrom).");
        var form = questionnaires.Find(derivedFrom);
        if (form is null || form.Adaptive)
            throw new RequestRejected($"The contained Questionnaire derives from a questionnaire the plan does not hold: {derivedFrom}.");

        var next = NextSet(form.Source, contained.Item.Select(i => i.LinkId).ToHashSet(), Answers(response.Item)).ToList();
        var updated = (QuestionnaireResponse)response.DeepCopy();
        var updatedContained = updated.Contained.OfType<Questionnaire>().First();
        updatedContained.Item.AddRange(next.Select(i => (Questionnaire.ItemComponent)i.DeepCopy()));
        updated.Status = next.Count == 0
            ? QuestionnaireResponse.QuestionnaireResponseStatus.Completed
            : QuestionnaireResponse.QuestionnaireResponseStatus.InProgress;

        return new Parameters
        {
            Meta = new Meta { Profile = [NextQuestionOutputProfile] },
            Parameter = [new Parameters.ParameterComponent { Name = "return", Resource = updated }],
        };
    }

    /// <summary>The next set of the source's top-level items, given which were handed out and what was answered.</summary>
    private static IEnumerable<Questionnaire.ItemComponent> NextSet(
        Questionnaire source,
        IReadOnlySet<string> delivered,
        IReadOnlyDictionary<string, List<DataType>> answers)
    {
        var dependedOn = source.Item.SelectMany(i => i.EnableWhen).Select(e => e.Question).ToHashSet();
        foreach (var item in source.Item.Where(i => !delivered.Contains(i.LinkId)))
        {
            if (!Enabled(item, answers)) continue;
            yield return item;
            if (dependedOn.Contains(item.LinkId)) yield break;
        }
    }

    /// <summary>FHIR enableWhen over the answers given: all conditions must hold (any, when the item says so).</summary>
    private static bool Enabled(Questionnaire.ItemComponent item, IReadOnlyDictionary<string, List<DataType>> answers)
    {
        if (item.EnableWhen.Count == 0) return true;
        var results = item.EnableWhen.Select(condition => Holds(condition, answers.GetValueOrDefault(condition.Question) ?? []));
        return item.EnableBehavior == Questionnaire.EnableWhenBehavior.Any ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Holds(Questionnaire.EnableWhenComponent condition, List<DataType> given) =>
        condition.Operator switch
        {
            Questionnaire.QuestionnaireItemOperator.Exists =>
                (condition.Answer as FhirBoolean)?.Value != false == given.Count > 0,
            Questionnaire.QuestionnaireItemOperator.Equal => given.Any(answer => SameValue(answer, condition.Answer)),
            Questionnaire.QuestionnaireItemOperator.NotEqual => given.Count > 0 && given.All(answer => !SameValue(answer, condition.Answer)),
            _ => false, // Ordering operators are not used by the plan's questionnaires.
        };

    private static bool SameValue(DataType given, DataType expected) => (given, expected) switch
    {
        (Coding a, Coding b) => a.Code == b.Code && (a.System == b.System || a.System is null || b.System is null),
        (PrimitiveType a, PrimitiveType b) => Equals(a.ObjectValue, b.ObjectValue),
        _ => given.IsExactly(expected),
    };

    /// <summary>Every answer in the response, by linkId, nested items included.</summary>
    private static Dictionary<string, List<DataType>> Answers(IEnumerable<QuestionnaireResponse.ItemComponent> items)
    {
        var answers = new Dictionary<string, List<DataType>>();
        void Collect(IEnumerable<QuestionnaireResponse.ItemComponent> level)
        {
            foreach (var item in level)
            {
                if (item.Answer.Count > 0)
                    answers[item.LinkId] = [.. item.Answer.Select(a => a.Value).OfType<DataType>()];
                Collect(item.Item);
                foreach (var answer in item.Answer) Collect(answer.Item);
            }
        }
        Collect(items);
        return answers;
    }
}
