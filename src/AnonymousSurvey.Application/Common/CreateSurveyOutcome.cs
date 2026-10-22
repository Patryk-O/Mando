using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Common;

public sealed record CreateSurveyOutcome(ValidationResult Validation, CreateSurveyResultDto? Result)
{
    public bool Succeeded => Validation.IsValid && Result is not null;
}
