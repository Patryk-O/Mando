using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record QuestionPoolItemDto(
    int QuestionId,
    string Text,
    QuestionType Type,
    bool IsActive);
