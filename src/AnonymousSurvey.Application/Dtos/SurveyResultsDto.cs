namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyResultsDto(
    int SurveyId,
    string SurveyTitle,
    int TotalSessions,
    IReadOnlyList<ScaleQuestionAggregateDto> ScaleAggregates,
    IReadOnlyList<YesNoQuestionAggregateDto> YesNoAggregates,
    IReadOnlyList<OpenQuestionAggregateDto> OpenAggregates,
    IReadOnlyList<SurveyResponseRowDto> RawResponses);
