namespace Cms0057.PriorAuthorization.Tests;

/// <summary>The Documentation Questionnaires the plan is configured with, by canonical URL.</summary>
public static class Questionnaires
{
    public const string ResidentialTreatment =
        "http://example.org/behavioral-health-plan/Questionnaire/residential-treatment";

    /// <summary>The same questionnaire taken as an adaptive form.</summary>
    public const string ResidentialTreatmentAdaptive = ResidentialTreatment + "-adaptive";
}
