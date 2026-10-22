using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Domain.Entities;

public class Survey
{
    public int Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public SurveyStatus Status { get; set; } = SurveyStatus.Draft;

    public bool IsDefault { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public string ResultsAccessToken { get; set; } = string.Empty;

    public string ConfigAccessToken { get; set; } = string.Empty;

    public ICollection<SurveyQuestion> SurveyQuestions { get; set; } = new List<SurveyQuestion>();

    public ICollection<ResponseSession> ResponseSessions { get; set; } = new List<ResponseSession>();
}
