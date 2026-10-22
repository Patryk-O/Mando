using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveyConfigService
{
    Task<SurveyConfigDto?> GetByConfigTokenAsync(
        string configAccessToken,
        CancellationToken cancellationToken = default);

    Task<UpdateSurveyConfigOutcome> UpdateAsync(
        string configAccessToken,
        UpdateSurveyConfigDto update,
        CancellationToken cancellationToken = default);
}
