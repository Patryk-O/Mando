namespace AnonymousSurvey.Application.Dtos;

public sealed record SubmitSurveyResponseDto(
    int SurveyId,
    IReadOnlyList<AnswerInputDto> Answers);
