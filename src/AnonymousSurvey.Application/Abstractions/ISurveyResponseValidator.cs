using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveyResponseValidator
{
    ValidationResult Validate(AnswerInputDto answer, SurveyQuestionDto question);

    ValidationResult Validate(
        IReadOnlyList<AnswerInputDto> answers,
        IReadOnlyList<SurveyQuestionDto> questions);
}
