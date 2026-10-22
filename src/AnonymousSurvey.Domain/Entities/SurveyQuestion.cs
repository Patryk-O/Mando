namespace AnonymousSurvey.Domain.Entities;

public class SurveyQuestion
{
    public int SurveyId { get; set; }

    public Survey Survey { get; set; } = null!;

    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    public int SortOrder { get; set; }
}
