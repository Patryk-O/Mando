namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyConfigDto(
    int SurveyId,
    string Title,
    IReadOnlyList<SurveyConfigQuestionDto> AssignedQuestions,
    IReadOnlyList<QuestionPoolItemDto> QuestionPool);
