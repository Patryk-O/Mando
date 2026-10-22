using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record CreateSurveyDto(
    string Title,
    string? Description,
    string? Slug,
    SurveyStatus Status,
    IReadOnlyList<SurveyItemInputDto> Items);
