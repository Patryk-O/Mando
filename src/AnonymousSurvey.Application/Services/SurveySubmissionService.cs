using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Application.Services;

public sealed class SurveySubmissionService(
    ISurveyQuestionQuery questionQuery,
    ISurveyResponseValidator validator,
    ISurveyResponseSaver saver) : ISurveySubmissionService
{
    public const string SurveyClosedMessage = "Ta ankieta została zamknięta i nie przyjmuje już odpowiedzi.";
    public const string FormChangedMessage = "Ankieta została zmieniona w trakcie wypełniania. Sprawdź formularz i wyślij ponownie.";

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

        var answersToSave = submission.Answers.Where(a => !a.IsEmpty).ToList();

        try
        {
            var saved = await saver.SaveAsync(submission with { Answers = answersToSave }, cancellationToken);
            return new SubmissionResult(ValidationResult.Success(), saved.SessionId);
        }
        catch (SurveyChangedException ex)
        {
            return new SubmissionResult(ValidationResult.Failure(MessageFor(ex.Conflict)), null, ex.Conflict);
        }
    }

    private static string MessageFor(SubmissionConflict conflict) => conflict switch
    {
        SubmissionConflict.SurveyClosed => SurveyClosedMessage,
        _ => FormChangedMessage
    };
}