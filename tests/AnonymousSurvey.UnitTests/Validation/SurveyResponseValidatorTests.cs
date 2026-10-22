using AnonymousSurvey.Application.Common;
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

    private const string AtLeastOneAnswerMessage = "Odpowiedz na co najmniej jedno pytanie.";

    private static SurveyQuestionDto Question(int id, QuestionType type, bool isRequired, string? text = null, int? sortOrder = null) =>
        new(id, text ?? $"Pytanie {id}", type, sortOrder ?? id, isRequired);

    // typ pytania, wymagane, ScaleValue, BoolValue, TextValue, czy odpowiedź ma przejść walidację
    public static TheoryData<QuestionType, bool, int?, bool?, string?, bool> AnswerMatrix()
    {
        var data = new TheoryData<QuestionType, bool, int?, bool?, string?, bool>();

        foreach (var required in new[] { true, false })
        {
            // Scale: brak odpowiedzi jest poprawny tylko dla pytania opcjonalnego; wartości poza 1–5 zawsze błędne.
            data.Add(QuestionType.Scale, required, null, null, null, !required);
            foreach (var invalid in new int[] { -1, 0, 6 })
            {
                data.Add(QuestionType.Scale, required, invalid, null, null, false);
            }

            foreach (var valid in new int[] { 1, 2, 3, 4, 5 })
            {
                data.Add(QuestionType.Scale, required, valid, null, null, true);
            }

            // YesNo
            data.Add(QuestionType.YesNo, required, null, null, null, !required);
            data.Add(QuestionType.YesNo, required, null, true, null, true);
            data.Add(QuestionType.YesNo, required, null, false, null, true);

            // Open: puste i same białe znaki = brak odpowiedzi.
            data.Add(QuestionType.Open, required, null, null, null, !required);
            data.Add(QuestionType.Open, required, null, null, "", !required);
            data.Add(QuestionType.Open, required, null, null, "  ", !required);
            data.Add(QuestionType.Open, required, null, null, "\t\n", !required);
            data.Add(QuestionType.Open, required, null, null, new string(' ', 2001), !required);
            data.Add(QuestionType.Open, required, null, null, "a", true);
            data.Add(QuestionType.Open, required, null, null, new string('a', 1999), true);
            data.Add(QuestionType.Open, required, null, null, new string('a', 2000), true);
            data.Add(QuestionType.Open, required, null, null, new string('a', 2001), false);

            // Emoji to dwie jednostki UTF-16 (jak w nvarchar): 1000 emoji = 2000 znaków.
            data.Add(QuestionType.Open, required, null, null, string.Concat(Enumerable.Repeat("😀", 1000)), true);
            data.Add(QuestionType.Open, required, null, null, string.Concat(Enumerable.Repeat("😀", 1000)) + "a", false);
            data.Add(QuestionType.Open, required, null, null, string.Concat(Enumerable.Repeat("😀", 1001)), false);

            // Mieszanie wartości różnych typów jest błędem także dla pytań opcjonalnych.
            data.Add(QuestionType.Scale, required, 3, null, "tekst", false);
            data.Add(QuestionType.Scale, required, 3, true, null, false);
            data.Add(QuestionType.YesNo, required, 3, true, null, false);
            data.Add(QuestionType.YesNo, required, null, true, "tekst", false);
            data.Add(QuestionType.Open, required, 3, null, "tekst", false);
            data.Add(QuestionType.Open, required, null, true, "tekst", false);

            // Wartość spoza typu bez właściwej wartości także nie przechodzi.
            data.Add(QuestionType.Scale, required, null, null, "tekst", false);
            data.Add(QuestionType.YesNo, required, null, null, "tekst", false);
            data.Add(QuestionType.Open, required, null, true, null, false);
            data.Add(QuestionType.Open, required, 3, null, null, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AnswerMatrix))]
    public void Validate_single_answer_matrix(
        QuestionType type, bool isRequired, int? scale, bool? boolValue, string? text, bool expectedValid)
    {
        var question = Question(1, type, isRequired);
        var answer = new AnswerInputDto(1, scale, boolValue, text);

        var result = _validator.Validate(answer, question);

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(expectedValid, result.Errors.Count == 0);
    }

    [Theory]
    [MemberData(nameof(AnswerMatrix))]
    public void Validate_collection_single_question_matrix_matches_single_answer_rules(
        QuestionType type, bool isRequired, int? scale, bool? boolValue, string? text, bool expectedValid)
    {
        // Drugie pytanie wymagane i poprawnie wypełnione, żeby wynik zależał tylko od pytania 1.
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, type, isRequired),
            Question(2, QuestionType.YesNo, true)
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, scale, boolValue, text),
            new(2, null, true, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(QuestionType.Scale, "Wybierz wartość na skali 1–5")]
    [InlineData(QuestionType.YesNo, "Wybierz Tak lub Nie")]
    [InlineData(QuestionType.Open, "Uzupełnij odpowiedź tekstową")]
    public void Required_empty_answer_keeps_type_specific_message(QuestionType type, string expectedFragment)
    {
        var question = Question(1, type, isRequired: true, text: "Ocena");

        var result = _validator.Validate(new AnswerInputDto(1, null, null, null), question);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Contains(expectedFragment, error, StringComparison.Ordinal);
        Assert.Contains("„Ocena”", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Question_dto_is_required_by_default()
    {
        var question = new SurveyQuestionDto(1, "Ocena", QuestionType.Scale, 1);

        Assert.True(question.IsRequired);
        Assert.False(_validator.Validate(new AnswerInputDto(1, null, null, null), question).IsValid);
    }

    [Fact]
    public void Single_answer_with_mismatched_question_id_is_rejected_even_when_optional_and_empty()
    {
        var question = Question(1, QuestionType.Scale, isRequired: false);

        var result = _validator.Validate(new AnswerInputDto(99, null, null, null), question);

        Assert.False(result.IsValid);
        Assert.Contains("nie pasuje", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_scale_may_be_skipped()
    {
        var result = _validator.Validate(
            new AnswerInputDto(1, null, null, null), Question(1, QuestionType.Scale, isRequired: false));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Optional_yes_no_may_be_skipped()
    {
        var result = _validator.Validate(
            new AnswerInputDto(1, null, null, null), Question(1, QuestionType.YesNo, isRequired: false));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Optional_open_may_be_blank()
    {
        var question = Question(1, QuestionType.Open, isRequired: false);

        Assert.True(_validator.Validate(new AnswerInputDto(1, null, null, null), question).IsValid);
        Assert.True(_validator.Validate(new AnswerInputDto(1, null, null, ""), question).IsValid);
        Assert.True(_validator.Validate(new AnswerInputDto(1, null, null, "   "), question).IsValid);
    }

    [Fact]
    public void Optional_scale_still_rejects_out_of_range()
    {
        var question = Question(1, QuestionType.Scale, isRequired: false);

        var result = _validator.Validate(new AnswerInputDto(1, 6, null, null), question);

        Assert.False(result.IsValid);
        Assert.Contains("1–5", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_open_still_rejects_text_over_limit()
    {
        var question = Question(1, QuestionType.Open, isRequired: false);

        var result = _validator.Validate(
            new AnswerInputDto(1, null, null, new string('a', SurveyLimits.MaxOpenAnswerLength + 1)), question);

        Assert.False(result.IsValid);
        Assert.Contains("2000", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Open_answer_limit_comes_from_survey_limits()
    {
        Assert.Equal(SurveyLimits.MaxOpenAnswerLength, SurveyResponseValidator.MaxTextLength);
        Assert.Equal(2000, SurveyLimits.MaxOpenAnswerLength);
    }

    [Fact]
    public void Required_open_blank_is_rejected()
    {
        var question = Question(1, QuestionType.Open, isRequired: true);

        Assert.False(_validator.Validate(new AnswerInputDto(1, null, null, null), question).IsValid);
        Assert.False(_validator.Validate(new AnswerInputDto(1, null, null, ""), question).IsValid);
        Assert.False(_validator.Validate(new AnswerInputDto(1, null, null, "   "), question).IsValid);
    }

    [Fact]
    public void Form_with_only_skipped_optional_questions_is_rejected()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: false),
            Question(2, QuestionType.Open, isRequired: false)
        };

        var result = _validator.Validate([], questions);

        Assert.False(result.IsValid);
        Assert.Contains(AtLeastOneAnswerMessage, result.Errors);
    }

    [Fact]
    public void Form_with_only_empty_answers_to_optional_questions_is_rejected()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: false),
            Question(2, QuestionType.Open, isRequired: false)
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, null, null, null),
            new(2, null, null, "   ")
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(AtLeastOneAnswerMessage, result.Errors);
    }

    [Fact]
    public void Form_with_one_answered_optional_question_is_valid_when_nothing_is_required()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: false),
            Question(2, QuestionType.Open, isRequired: false)
        };
        var answers = new List<AnswerInputDto> { new(2, null, null, "Tylko komentarz") };

        var result = _validator.Validate(answers, questions);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Form_allows_skipped_optional_when_required_answered()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: true),
            Question(2, QuestionType.Open, isRequired: false),
            Question(3, QuestionType.YesNo, isRequired: false)
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, 4, null, null),
            new(3, null, null, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Form_rejects_missing_required_even_if_optional_answered()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: true, text: "Ocena"),
            Question(2, QuestionType.Open, isRequired: false)
        };
        var answers = new List<AnswerInputDto> { new(2, null, null, "Komentarz") };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("Brak odpowiedzi na pytanie: „Ocena”", StringComparison.Ordinal));
        Assert.DoesNotContain(AtLeastOneAnswerMessage, result.Errors);
    }

    [Fact]
    public void Form_rejects_empty_answer_to_required_question()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: true),
            Question(2, QuestionType.Open, isRequired: false)
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, null, null, null),
            new(2, null, null, "Komentarz")
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Wybierz wartość na skali", StringComparison.Ordinal));
    }

    [Fact]
    public void Form_rejects_invalid_answer_to_optional_question()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: true),
            Question(2, QuestionType.Scale, isRequired: false)
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, 3, null, null),
            new(2, 9, null, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("1–5", StringComparison.Ordinal));
    }

    [Fact]
    public void Form_rejects_duplicate_answers_for_optional_question()
    {
        var questions = new List<SurveyQuestionDto>
        {
            Question(1, QuestionType.Scale, isRequired: true),
            Question(2, QuestionType.Scale, isRequired: false, text: "Dodatkowa")
        };
        var answers = new List<AnswerInputDto>
        {
            new(1, 3, null, null),
            new(2, 4, null, null),
            new(2, 5, null, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("więcej niż jedną odpowiedź", StringComparison.Ordinal));
    }

    [Fact]
    public void Form_rejects_answer_to_unknown_question()
    {
        var questions = new List<SurveyQuestionDto> { Question(1, QuestionType.Scale, isRequired: true) };
        var answers = new List<AnswerInputDto>
        {
            new(1, 3, null, null),
            new(77, 3, null, null)
        };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("nieznane pytanie (Id 77)", StringComparison.Ordinal));
    }

    [Fact]
    public void Form_with_only_answer_to_unknown_question_does_not_satisfy_at_least_one()
    {
        var questions = new List<SurveyQuestionDto> { Question(1, QuestionType.Scale, isRequired: false) };
        var answers = new List<AnswerInputDto> { new(77, 3, null, null) };

        var result = _validator.Validate(answers, questions);

        Assert.False(result.IsValid);
        Assert.Contains(AtLeastOneAnswerMessage, result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("nieznane pytanie", StringComparison.Ordinal));
    }

    [Fact]
    public void Form_without_questions_is_rejected()
    {
        var result = _validator.Validate([], []);

        Assert.False(result.IsValid);
        Assert.Equal("Ankieta nie zawiera pytań.", Assert.Single(result.Errors));
    }
}
