using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// DTR: a provider system's DTR app asks the Behavioral Health Plan what documentation a request needs, and is
/// handed the Documentation Questionnaire to complete with a started response.
/// </summary>
public sealed class DocumentationRequirementsTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Asking_with_a_residential_treatment_order_is_answered_with_the_plans_questionnaire_and_a_started_response()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage("H0019", "U1"));

        Assert.Equal((HttpStatusCode.OK, 1), (response.Status, response.Packages.Count));
        Assert.Equal((Questionnaires.ResidentialTreatment, "Residential Treatment Documentation"),
            (response.QuestionnaireUrl, response.QuestionnaireTitle));
        Assert.Equal(("in-progress", Questionnaires.ResidentialTreatment, "Patient/MemberExample"),
            (response.QuestionnaireResponseStatus, response.QuestionnaireResponseQuestionnaire, response.QuestionnaireResponseSubject));
    }

    [Fact]
    public async Task The_started_response_says_which_coverage_and_order_it_is_for_and_that_it_goes_with_the_request()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage("H0019", "U1"));

        Assert.Equal(("Coverage/MemberCoverage", "ServiceRequest/ResidentialOrder", "withpa"),
            (response.QuestionnaireResponseCoverage, response.QuestionnaireResponseContext, response.IntendedUse));
    }

    [Fact]
    public async Task The_plan_fills_in_the_service_the_order_asks_for()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage("H0019", "U2"));

        Assert.Equal("H0019 U2", response.Answer("requested-service"));
    }

    [Fact]
    public async Task The_questionnaire_asks_for_the_level_of_care_assessment_and_the_stay_requested()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage());

        Assert.Equal(
            ["requested-service", "asam-level", "asam-assessment-date", "diagnosis", "admission-date", "length-of-stay",
                "less-intensive-care", "prior-residential", "prior-residential-episode"],
            response.QuestionnaireItems);
    }

    [Fact]
    public async Task A_questionnaire_can_be_asked_for_by_its_canonical_without_an_order()
    {
        var response = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage(orderCode: null, questionnaire: Questionnaires.ResidentialTreatment));

        Assert.Equal((1, Questionnaires.ResidentialTreatment, null),
            (response.Packages.Count, response.QuestionnaireUrl, response.QuestionnaireResponseContext));
    }

    [Fact]
    public async Task Asking_for_the_same_questionnaire_by_canonical_and_by_order_gives_one_package()
    {
        var response = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatment));

        Assert.Single(response.Packages);
    }

    [Fact]
    public async Task An_order_for_a_service_the_plan_does_not_prior_authorize_needs_no_documentation()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage("H0004", "U7"));

        Assert.Equal((HttpStatusCode.OK, 0), (response.Status, response.Packages.Count));
        Assert.Equal("No questionnaire matched the request: name one the plan holds, or an order for a service it prior-authorizes.",
            response.OutcomeMessage);
    }

    [Fact]
    public async Task A_questionnaire_the_plan_does_not_hold_is_named_in_the_outcome()
    {
        var response = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage(orderCode: null, questionnaire: "http://other-payer.example.test/Questionnaire/x"));

        Assert.Empty(response.Packages);
        Assert.Contains("http://other-payer.example.test/Questionnaire/x", response.Body);
    }

    // Adaptive forms: the plan hands out the questions a few at a time and chooses the next ones from the answers
    // so far. Every questionnaire the plan holds is also offered this way, under its canonical with "-adaptive".

    [Fact]
    public async Task Asking_for_the_adaptive_form_is_answered_with_its_first_questions_and_where_the_next_come_from()
    {
        var response = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatmentAdaptive));

        // Asked for by name with the order it is for: one package, the adaptive form, about that order.
        Assert.Equal((1, Questionnaires.ResidentialTreatmentAdaptive, "in-progress", "ServiceRequest/ResidentialOrder", "H0019 U1"),
            (response.Packages.Count, response.QuestionnaireUrl, response.QuestionnaireResponseStatus,
                response.QuestionnaireResponseContext, response.Answer("requested-service")));
        Assert.Equal(
            ["requested-service", "asam-level", "asam-assessment-date", "diagnosis", "admission-date", "length-of-stay",
                "less-intensive-care", "prior-residential"],
            response.ContainedQuestionnaireItems);
        Assert.Equal(("http://localhost/fhir/Questionnaire/$next-question", Questionnaires.ResidentialTreatment + "|0.1.0"),
            (response.NextQuestionEndpoint, response.ContainedQuestionnaireDerivedFrom));
    }

    [Fact]
    public async Task Answering_that_the_member_had_residential_treatment_brings_the_questions_about_it()
    {
        var package = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatmentAdaptive));

        var next = await _api.NextQuestion(DtrRequests.NextQuestion(package.QuestionnaireResponseJson!,
            ("prior-residential", """{ "valueBoolean": true }""")));

        Assert.Equal((HttpStatusCode.OK, "in-progress"), (next.Status, next.QuestionnaireResponseStatus));
        Assert.Equal("prior-residential-episode", next.ContainedQuestionnaireItems.Last());
        Assert.Equal(9, next.ContainedQuestionnaireItems.Count);
    }

    [Fact]
    public async Task Answering_that_the_member_had_none_completes_the_form()
    {
        var package = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatmentAdaptive));

        var next = await _api.NextQuestion(DtrRequests.NextQuestion(package.QuestionnaireResponseJson!,
            ("prior-residential", """{ "valueBoolean": false }""")));

        Assert.Equal((HttpStatusCode.OK, "completed", 8), (next.Status, next.QuestionnaireResponseStatus, next.ContainedQuestionnaireItems.Count));
    }

    [Fact]
    public async Task The_answers_given_so_far_come_back_with_the_next_questions()
    {
        var package = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatmentAdaptive));

        var next = await _api.NextQuestion(DtrRequests.NextQuestion(package.QuestionnaireResponseJson!,
            ("length-of-stay", """{ "valueInteger": 30 }"""), ("prior-residential", """{ "valueBoolean": true }""")));

        Assert.Equal(("30", "H0019 U1"), (next.Answer("length-of-stay"), next.Answer("requested-service")));
    }

    [Fact]
    public async Task A_response_to_a_questionnaire_the_plan_does_not_hold_is_rejected()
    {
        var package = await _api.QuestionnairePackage(
            DtrRequests.QuestionnairePackage("H0019", "U1", questionnaire: Questionnaires.ResidentialTreatmentAdaptive));
        var foreign = package.QuestionnaireResponseJson!.Replace(Questionnaires.ResidentialTreatment + "|0.1.0", "http://other-payer.example.test/Questionnaire/x");

        var next = await _api.NextQuestion(DtrRequests.NextQuestion(foreign));

        Assert.Equal((HttpStatusCode.BadRequest, "OperationOutcome"), (next.Status, next.ResourceType));
    }

    [Fact]
    public async Task A_response_without_its_questionnaire_contained_is_rejected()
    {
        var next = await _api.NextQuestion(DtrRequests.NextQuestion("""{ "resourceType": "QuestionnaireResponse", "status": "in-progress" }"""));

        Assert.Equal((HttpStatusCode.BadRequest, "OperationOutcome"), (next.Status, next.ResourceType));
    }

    [Fact]
    public async Task Asking_without_the_members_coverage_is_rejected()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage(coverage: false));

        Assert.Equal((HttpStatusCode.BadRequest, "OperationOutcome"), (response.Status, response.ResourceType));
    }

    [Fact]
    public async Task A_system_without_the_scope_to_read_questionnaires_is_refused()
    {
        var response = await _api.QuestionnairePackage(DtrRequests.QuestionnairePackage(), Caller.As(_api.Ehr));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }
}
