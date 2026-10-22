namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyConfigAssignmentDto(int QuestionId, int SortOrder);

public sealed record UpdateSurveyConfigDto(
    int SurveyId,
    IReadOnlyList<SurveyConfigAssignmentDto> Assignments);
