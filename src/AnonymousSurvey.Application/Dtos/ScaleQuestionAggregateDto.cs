namespace AnonymousSurvey.Application.Dtos;

public sealed record ScaleQuestionAggregateDto(
    int QuestionId,
    string Text,
    double Average,
    IReadOnlyDictionary<int, int> Distribution);
