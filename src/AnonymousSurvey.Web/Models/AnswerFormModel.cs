using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Web.Models;

public sealed class AnswerFormModel(int questionId)
{
    public int QuestionId { get; } = questionId;

    public int? ScaleValue { get; set; }

    public bool? BoolValue { get; set; }

    public string? TextValue { get; set; }

    public AnswerInputDto ToDto() => new(QuestionId, ScaleValue, BoolValue, TextValue);

    public void Clear()
    {
        ScaleValue = null;
        BoolValue = null;
        TextValue = null;
    }

    /// <summary>
    /// Zostawia odpowiedzi pytań nadal obecnych w ankiecie (te same instancje) i dopisuje puste modele dla nowych.
    /// </summary>
    public static Dictionary<int, AnswerFormModel> CarryOver(
        IReadOnlyDictionary<int, AnswerFormModel> current,
        IEnumerable<SurveyQuestionDto> questions)
    {
        var next = new Dictionary<int, AnswerFormModel>();
        foreach (var question in questions)
        {
            next[question.QuestionId] = current.TryGetValue(question.QuestionId, out var existing)
                ? existing
                : new AnswerFormModel(question.QuestionId);
        }

        return next;
    }

    /// <summary>
    /// Prawda, gdy zestaw pytań widoczny na formularzu różni się od świeżego odczytu (identyfikator, treść, typ, kolejność, wymagane).
    /// </summary>
    public static bool ShapeChanged(
        IReadOnlyList<SurveyQuestionDto> displayed,
        IReadOnlyList<SurveyQuestionDto> fresh)
    {
        if (displayed.Count != fresh.Count)
        {
            return true;
        }

        var freshById = fresh.ToDictionary(question => question.QuestionId);
        foreach (var question in displayed)
        {
            if (!freshById.TryGetValue(question.QuestionId, out var updated)
                || updated.IsRequired != question.IsRequired
                || updated.Type != question.Type
                || updated.SortOrder != question.SortOrder
                || !string.Equals(updated.Text, question.Text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record ScaleChoice(int? Value, string Label);

public sealed record YesNoChoice(bool? Value, string Label);
