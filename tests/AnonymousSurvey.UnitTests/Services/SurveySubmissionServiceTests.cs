using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Application.Services;
using AnonymousSurvey.Application.Validation;
using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.UnitTests.Services;

public class SurveySubmissionServiceTests
{
    private const int SurveyId = 7;

    private static readonly SurveyQuestionDto Scale = new(1, "Ocena", QuestionType.Scale, 1, IsRequired: true);
    private static readonly SurveyQuestionDto OptionalOpen = new(2, "Komentarz", QuestionType.Open, 2, IsRequired: false);
    private static readonly SurveyQuestionDto OptionalYesNo = new(3, "Polecasz?", QuestionType.YesNo, 3, IsRequired: false);

    private readonly FakeQuestionQuery _questionQuery = new(Scale, OptionalOpen, OptionalYesNo);
    private readonly FakeSaver _saver = new();

    private SurveySubmissionService CreateService(ISurveyResponseValidator? validator = null) =>
        new(_questionQuery, validator ?? new SurveyResponseValidator(), _saver);

    [Fact]
    public async Task Submission_filters_empty_optional_answers_before_save()
    {
        var submission = new SubmitSurveyResponseDto(SurveyId,
        [
            new AnswerInputDto(1, 4, null, null),
            new AnswerInputDto(2, null, null, "   "),
            new AnswerInputDto(3, null, null, null)
        ]);

        var result = await CreateService().SubmitAsync(submission);

        Assert.True(result.Succeeded);
        Assert.Equal(_saver.SessionId, result.SessionId);
        Assert.Equal(SubmissionConflict.None, result.Conflict);
        var saved = Assert.Single(_saver.Submissions);
        Assert.Equal(SurveyId, saved.SurveyId);
        var answer = Assert.Single(saved.Answers);
        Assert.Equal(1, answer.QuestionId);
        Assert.Equal(4, answer.ScaleValue);
    }

    [Fact]
    public async Task Submission_keeps_non_empty_optional_answers()
    {
        var submission = new SubmitSurveyResponseDto(SurveyId,
        [
            new AnswerInputDto(1, 5, null, null),
            new AnswerInputDto(2, null, null, "Świetnie 😀"),
            new AnswerInputDto(3, null, false, null)
        ]);

        var result = await CreateService().SubmitAsync(submission);

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_saver.Submissions);
        Assert.Equal([1, 2, 3], saved.Answers.Select(a => a.QuestionId).ToArray());
        Assert.Equal("Świetnie 😀", saved.Answers[1].TextValue);
        Assert.Equal(false, saved.Answers[2].BoolValue);
    }

    [Fact]
    public async Task Submission_with_omitted_optional_answers_is_saved_without_them()
    {
        var submission = new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 2, null, null)]);

        var result = await CreateService().SubmitAsync(submission);

        Assert.True(result.Succeeded);
        Assert.Single(Assert.Single(_saver.Submissions).Answers);
    }

    [Fact]
    public async Task Submission_does_not_call_saver_when_invalid()
    {
        var submission = new SubmitSurveyResponseDto(SurveyId,
        [
            new AnswerInputDto(2, null, null, "Tylko komentarz")
        ]);

        var result = await CreateService().SubmitAsync(submission);

        Assert.False(result.Succeeded);
        Assert.False(result.Validation.IsValid);
        Assert.Null(result.SessionId);
        Assert.Equal(SubmissionConflict.None, result.Conflict);
        Assert.Empty(_saver.Submissions);
    }

    [Fact]
    public async Task Submission_does_not_call_saver_when_only_optional_questions_skipped()
    {
        _questionQuery.Questions = [OptionalOpen, OptionalYesNo];
        var submission = new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(2, null, null, "  ")]);

        var result = await CreateService().SubmitAsync(submission);

        Assert.False(result.Succeeded);
        Assert.Contains("Odpowiedz na co najmniej jedno pytanie.", result.Validation.Errors);
        Assert.Empty(_saver.Submissions);
    }

    [Fact]
    public async Task Submission_does_not_call_saver_when_validator_rejects()
    {
        var validator = new StubValidator(ValidationResult.Failure("błąd"));

        var result = await CreateService(validator).SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)]));

        Assert.False(result.Succeeded);
        Assert.Equal(["błąd"], result.Validation.Errors);
        Assert.Empty(_saver.Submissions);
        Assert.Equal(1, validator.CollectionCalls);
    }

    [Fact]
    public async Task Submission_validates_against_questions_of_requested_survey()
    {
        await CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)]));

        Assert.Equal([SurveyId], _questionQuery.RequestedSurveyIds);
    }

    [Fact]
    public async Task Submission_maps_survey_closed_exception_to_conflict()
    {
        _saver.Exception = new SurveyChangedException(SubmissionConflict.SurveyClosed);

        var result = await CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)]));

        Assert.False(result.Succeeded);
        Assert.Null(result.SessionId);
        Assert.Equal(SubmissionConflict.SurveyClosed, result.Conflict);
        Assert.False(result.Validation.IsValid);
        Assert.Equal(
            "Ta ankieta została zamknięta i nie przyjmuje już odpowiedzi.",
            Assert.Single(result.Validation.Errors));
    }

    [Fact]
    public async Task Submission_maps_form_changed_exception_to_conflict()
    {
        _saver.Exception = new SurveyChangedException(SubmissionConflict.FormChanged);

        var result = await CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)]));

        Assert.False(result.Succeeded);
        Assert.Null(result.SessionId);
        Assert.Equal(SubmissionConflict.FormChanged, result.Conflict);
        Assert.Equal(
            "Ankieta została zmieniona w trakcie wypełniania. Sprawdź formularz i wyślij ponownie.",
            Assert.Single(result.Validation.Errors));
    }

    [Fact]
    public async Task Submission_does_not_swallow_other_exceptions()
    {
        _saver.Exception = new InvalidOperationException("awaria bazy");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)])));

        Assert.Equal("awaria bazy", ex.Message);
    }

    [Fact]
    public async Task Submission_does_not_swallow_exception_from_question_query()
    {
        _questionQuery.Exception = new TimeoutException("zapytanie");

        await Assert.ThrowsAsync<TimeoutException>(() => CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)])));

        Assert.Empty(_saver.Submissions);
    }

    [Fact]
    public async Task Submission_propagates_cancellation_token()
    {
        using var cts = new CancellationTokenSource();

        await CreateService().SubmitAsync(
            new SubmitSurveyResponseDto(SurveyId, [new AnswerInputDto(1, 3, null, null)]), cts.Token);

        Assert.Equal(cts.Token, _questionQuery.LastToken);
        Assert.Equal(cts.Token, _saver.LastToken);
    }

    private sealed class FakeQuestionQuery(params SurveyQuestionDto[] questions) : ISurveyQuestionQuery
    {
        public IReadOnlyList<SurveyQuestionDto> Questions { get; set; } = questions;
        public Exception? Exception { get; set; }
        public List<int> RequestedSurveyIds { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task<IReadOnlyList<SurveyQuestionDto>> GetQuestionsAsync(
            int surveyId, CancellationToken cancellationToken = default)
        {
            RequestedSurveyIds.Add(surveyId);
            LastToken = cancellationToken;
            return Exception is null ? Task.FromResult(Questions) : throw Exception;
        }

        public Task<SurveyQuestionDto?> GetQuestionAsync(
            int surveyId, int questionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Questions.FirstOrDefault(q => q.QuestionId == questionId));
    }

    private sealed class FakeSaver : ISurveyResponseSaver
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public Exception? Exception { get; set; }
        public List<SubmitSurveyResponseDto> Submissions { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task<SubmitSurveyResponseResultDto> SaveAsync(
            SubmitSurveyResponseDto submission, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            if (Exception is not null)
            {
                throw Exception;
            }

            Submissions.Add(submission);
            return Task.FromResult(new SubmitSurveyResponseResultDto(SessionId));
        }
    }

    private sealed class StubValidator(ValidationResult collectionResult) : ISurveyResponseValidator
    {
        public int CollectionCalls { get; private set; }

        public ValidationResult Validate(AnswerInputDto answer, SurveyQuestionDto question) =>
            ValidationResult.Success();

        public ValidationResult Validate(
            IReadOnlyList<AnswerInputDto> answers, IReadOnlyList<SurveyQuestionDto> questions)
        {
            CollectionCalls++;
            return collectionResult;
        }
    }
}
