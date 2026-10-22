namespace AnonymousSurvey.Application.Dtos;

/// <summary>
/// Dokładnie jedno z pól QuestionId / NewQuestion musi być ustawione.
/// Pozycja na liście wyznacza SortOrder (1..n).
/// </summary>
public sealed record SurveyItemInputDto(
    int? QuestionId,
    NewQuestionInputDto? NewQuestion,
    bool IsRequired);
