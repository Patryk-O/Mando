namespace AnonymousSurvey.Application.Common;

public sealed record SubmissionResult(ValidationResult Validation, Guid? SessionId)
{
    public bool Succeeded => Validation.IsValid && SessionId is not null;
}
