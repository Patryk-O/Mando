namespace AnonymousSurvey.Domain.Entities;

public class ResponseSession
{
    public Guid Id { get; set; }

    public int SurveyId { get; set; }

    public Survey Survey { get; set; } = null!;

    public DateTime SubmittedAtUtc { get; set; }

    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
}
