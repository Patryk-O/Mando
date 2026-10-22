using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Abstractions;

public interface ISurveySubmissionService
{
    Task<SubmissionResult> SubmitAsync(
        SubmitSurveyResponseDto submission,
        CancellationToken cancellationToken = default);
}
