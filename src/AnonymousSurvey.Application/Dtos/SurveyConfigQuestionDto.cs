using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyConfigQuestionDto(
    int QuestionId,
    string Text,
    QuestionType Type,
    int SortOrder,
    bool IsRequired,
    int AnswerCount);
