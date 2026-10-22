using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Validation;

public sealed class SurveyResponseValidator : ISurveyResponseValidator
{
    public const int MaxTextLength = 2000;

    public ValidationResult Validate(AnswerInputDto answer, SurveyQuestionDto question)
    {
        if (answer.QuestionId != question.QuestionId)
        {
            return ValidationResult.Failure(
                $"Odpowiedź nie pasuje do pytania (oczekiwane Id {question.QuestionId}, otrzymane {answer.QuestionId}).");
        }

        return question.Type switch
        {
            QuestionType.Scale => ValidateScale(answer, question),
            QuestionType.Open => ValidateOpen(answer, question),
            QuestionType.YesNo => ValidateYesNo(answer, question),
            _ => ValidationResult.Failure($"Nieobsługiwany typ pytania (Id {question.QuestionId}).")
        };
    }

    public ValidationResult Validate(
        IReadOnlyList<AnswerInputDto> answers,
        IReadOnlyList<SurveyQuestionDto> questions)
    {
        if (questions.Count == 0)
        {
            return ValidationResult.Failure("Ankieta nie zawiera pytań.");
        }

        var errors = new List<string>();
        var answersByQuestion = answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var question in questions.OrderBy(q => q.SortOrder))
        {
            if (!answersByQuestion.TryGetValue(question.QuestionId, out var questionAnswers))
            {
                errors.Add($"Brak odpowiedzi na pytanie: „{question.Text}”.");
                continue;
            }

            if (questionAnswers.Count > 1)
            {
                errors.Add($"Pytanie „{question.Text}” ma więcej niż jedną odpowiedź.");
                continue;
            }

            var singleResult = Validate(questionAnswers[0], question);
            errors.AddRange(singleResult.Errors);
        }

        var expectedIds = questions.Select(q => q.QuestionId).ToHashSet();
        foreach (var extraId in answersByQuestion.Keys.Where(id => !expectedIds.Contains(id)))
        {
            errors.Add($"Odpowiedź na nieznane pytanie (Id {extraId}).");
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    private static ValidationResult ValidateScale(AnswerInputDto answer, SurveyQuestionDto question)
    {
        if (answer.ScaleValue is null)
        {
            return ValidationResult.Failure($"Wybierz wartość na skali 1–5 dla pytania: „{question.Text}”.");
        }

        if (answer.ScaleValue is < 1 or > 5)
        {
            return ValidationResult.Failure(
                $"Skala dla pytania „{question.Text}” musi być w zakresie 1–5.");
        }

        if (answer.BoolValue is not null || !string.IsNullOrWhiteSpace(answer.TextValue))
        {
            return ValidationResult.Failure($"Pytanie „{question.Text}” wymaga tylko wartości skali.");
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateOpen(AnswerInputDto answer, SurveyQuestionDto question)
    {
        if (string.IsNullOrWhiteSpace(answer.TextValue))
        {
            return ValidationResult.Failure($"Uzupełnij odpowiedź tekstową dla pytania: „{question.Text}”.");
        }

        if (answer.TextValue.Length > MaxTextLength)
        {
            return ValidationResult.Failure(
                $"Odpowiedź na pytanie „{question.Text}” może mieć maksymalnie {MaxTextLength} znaków.");
        }

        if (answer.ScaleValue is not null || answer.BoolValue is not null)
        {
            return ValidationResult.Failure($"Pytanie „{question.Text}” wymaga tylko odpowiedzi tekstowej.");
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateYesNo(AnswerInputDto answer, SurveyQuestionDto question)
    {
        if (answer.BoolValue is null)
        {
            return ValidationResult.Failure($"Wybierz Tak lub Nie dla pytania: „{question.Text}”.");
        }

        if (answer.ScaleValue is not null || !string.IsNullOrWhiteSpace(answer.TextValue))
        {
            return ValidationResult.Failure($"Pytanie „{question.Text}” wymaga tylko odpowiedzi Tak/Nie.");
        }

        return ValidationResult.Success();
    }
}
