namespace AnonymousSurvey.Domain.Entities;

public class Survey
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string ResultsAccessToken { get; set; } = string.Empty;

    public string ConfigAccessToken { get; set; } = string.Empty;

    public ICollection<SurveyQuestion> SurveyQuestions { get; set; } = new List<SurveyQuestion>();

    public ICollection<ResponseSession> ResponseSessions { get; set; } = new List<ResponseSession>();
}
