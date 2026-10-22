using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Services;

public sealed class SurveySubmissionService(
    ISurveyQuestionQuery questionQuery,
    ISurveyResponseValidator validator,
    ISurveyResponseSaver saver) : ISurveySubmissionService
{
    public async Task<SubmissionResult> SubmitAsync(
        SubmitSurveyResponseDto submission,
        CancellationToken cancellationToken = default)
    {
        var questions = await questionQuery.GetQuestionsAsync(submission.SurveyId, cancellationToken);
        var validation = validator.Validate(submission.Answers, questions);

        if (!validation.IsValid)
        {
            return new SubmissionResult(validation, null);
        }

        var saved = await saver.SaveAsync(submission, cancellationToken);
        return new SubmissionResult(ValidationResult.Success(), saved.SessionId);
    }
}
