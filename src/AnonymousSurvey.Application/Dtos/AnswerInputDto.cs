namespace AnonymousSurvey.Application.Dtos;

public sealed record AnswerInputDto(
    int QuestionId,
    int? ScaleValue,
    bool? BoolValue,
    string? TextValue);
