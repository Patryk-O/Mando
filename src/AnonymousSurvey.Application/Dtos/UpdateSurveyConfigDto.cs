using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record UpdateSurveyConfigDto(
    int SurveyId,
    string Title,
    string? Description,
    SurveyStatus Status,
    bool IsDefault,
    byte[] RowVersion,
    IReadOnlyList<SurveyItemInputDto> Items);
