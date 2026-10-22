using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Domain.Entities;

public class Question
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public QuestionType Type { get; set; }

    public bool IsActive { get; set; }

    public ICollection<SurveyQuestion> SurveyQuestions { get; set; } = new List<SurveyQuestion>();

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
}
