namespace AnonymousSurvey.Application.Common;

public sealed record UpdateSurveyConfigOutcome(ValidationResult Validation, bool IsConcurrencyConflict = false)
{
    public bool Succeeded => Validation.IsValid && !IsConcurrencyConflict;
}
