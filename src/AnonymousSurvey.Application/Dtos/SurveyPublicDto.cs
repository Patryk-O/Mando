using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record SurveyPublicDto(
    int SurveyId,
    string Slug,
    string Title,
    string? Description,
    SurveyStatus Status);
