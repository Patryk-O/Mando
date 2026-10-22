namespace AnonymousSurvey.Domain.Entities;

public class Answer
{
    public Guid SessionId { get; set; }

    public ResponseSession Session { get; set; } = null!;

    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    public int? ScaleValue { get; set; }

    public bool? BoolValue { get; set; }

    public string? TextValue { get; set; }
}
