using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Application.Validation;
using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.UnitTests.Validation;

public class SurveyResponseValidatorTests
{
    private readonly SurveyResponseValidator _validator = new();

    [Fact]
    public void Validate_scale_answer_accepts_value_in_range()
    {
        var question = new SurveyQuestionDto(1, "Ocena", QuestionType.Scale, 1);
        var answer = new AnswerInputDto(1, 4, null, null);

        var result = _validator.Validate(answer, question);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_scale_answer_rejects_out_of_range()
    {
        var question = new SurveyQuestionDto(1, "Ocena", QuestionType.Scale, 1);
        var answer = new AnswerInputDto(1, 6, null, null);

        var result = _validator.Validate(answer, question);

        Assert.False(result.IsValid);
        Assert.Contains("1–5", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_open_answer_rejects_whitespace_only()
    {
        var question = new SurveyQuestionDto(2, "Komentarz", QuestionType.Open, 2);
        var answer = new AnswerInputDto(2, null, null, "   ");

        var result = _validator.Validate(answer, question);

        Assert.False(result.IsValid);
        Assert.Contains("tekstową", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_open_answer_rejects_text_longer_than_limit()
    {
        var question = new SurveyQuestionDto(2, "Komentarz", QuestionType.Open, 2);
        var answer = new AnswerInputDto(2, null, null, new string('a', SurveyResponseValidator.MaxTextLength + 1));

        var result = _validator.Validate(answer, question);

        Assert.False(result.IsValid);
        Assert.Contains("2000", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_yes_no_requires_choice()
    {
        var question = new SurveyQuestionDto(3, "Zgoda", QuestionType.YesNo, 3);
        var answer = new AnswerInputDto(3, null, null, null);

        var result = _validator.Validate(answer, question);

        Assert.False(result.IsValid);
        Assert.Contains("Tak lub Nie", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_collection_requires_all_questions()
    {
        var questions = new List<SurveyQuestionDto>
        {
            new(1, "Ocena", QuestionType.Scale, 1),
            new(2, "Komentarz", QuestionType.Open, 2)
        };

        var answers = new List<AnswerInputDto>
        {
            new(1, 3, null, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains("Brak odpowiedzi", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_collection_succeeds_when_complete()
    {
        var questions = new List<SurveyQuestionDto>
        {
            new(1, "Ocena", QuestionType.Scale, 1),
            new(2, "Komentarz", QuestionType.Open, 2),
            new(3, "Zgoda", QuestionType.YesNo, 3)
        };

        var answers = new List<AnswerInputDto>
        {
            new(1, 5, null, null),
            new(2, null, null, "Dobrze"),
            new(3, null, true, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.True(result.IsValid);
    }
}
