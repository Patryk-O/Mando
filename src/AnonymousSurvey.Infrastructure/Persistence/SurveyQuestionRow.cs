using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Infrastructure.Persistence;

/// <summary>
/// Wiersz zwracany przez funkcję tabelaryczną dbo.fn_GetSurveyQuestions (typ bez klucza).
/// </summary>
public sealed class SurveyQuestionRow
{
    public int SurveyId { get; set; }

    public int QuestionId { get; set; }

    public string Text { get; set; } = string.Empty;

    public QuestionType Type { get; set; }

    public int SortOrder { get; set; }
}
