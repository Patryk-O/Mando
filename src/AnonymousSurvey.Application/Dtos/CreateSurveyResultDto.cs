namespace AnonymousSurvey.Application.Dtos;

public sealed record CreateSurveyResultDto(
    int SurveyId,
    string Slug,
    string ResultsAccessToken,
    string ConfigAccessToken);
