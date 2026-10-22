namespace AnonymousSurvey.Application.Dtos;

public sealed record OpenQuestionAggregateDto(
    int QuestionId,
    string Text,
    int ResponseCount);
