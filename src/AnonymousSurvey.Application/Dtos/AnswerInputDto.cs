namespace AnonymousSurvey.Application.Dtos;

public sealed record AnswerInputDto(
    int QuestionId,
    int? ScaleValue,
    bool? BoolValue,
    string? TextValue)
{
    public bool IsEmpty =>
        ScaleValue is null && BoolValue is null && string.IsNullOrWhiteSpace(TextValue);
}
