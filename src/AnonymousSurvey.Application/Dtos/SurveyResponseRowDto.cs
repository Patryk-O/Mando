namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyResponseRowDto(
    Guid SessionId,
    DateTime SubmittedAtUtc,
    IReadOnlyList<AnswerInputDto> Answers);
