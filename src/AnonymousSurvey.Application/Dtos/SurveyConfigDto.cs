using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyConfigDto(
    int SurveyId,
    string Slug,
    string Title,
    string? Description,
    SurveyStatus Status,
    bool IsDefault,
    int ResponseCount,
    byte[] RowVersion,
    IReadOnlyList<SurveyConfigQuestionDto> AssignedQuestions,
    IReadOnlyList<QuestionPoolItemDto> QuestionPool);
