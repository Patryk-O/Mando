namespace AnonymousSurvey.Application.Common;

public enum SubmissionConflict
{
    None = 0,
    SurveyClosed = 1,
    FormChanged = 2
}

public sealed record SubmissionResult(
    ValidationResult Validation,
    Guid? SessionId,
    SubmissionConflict Conflict = SubmissionConflict.None)
{
    public bool Succeeded => Validation.IsValid && SessionId is not null;
}
