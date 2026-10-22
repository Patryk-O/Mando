namespace AnonymousSurvey.Application.Dtos;

public sealed record YesNoQuestionAggregateDto(
    int QuestionId,
    string Text,
    int YesCount,
    int NoCount,
    int AnsweredCount,
    int EligibleCount,
    bool IsCurrentlyAssigned);
