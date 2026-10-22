using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyQuestionDto(
    int QuestionId,
    string Text,
    QuestionType Type,
    int SortOrder);
