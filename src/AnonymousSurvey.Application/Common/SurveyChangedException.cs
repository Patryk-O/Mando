namespace AnonymousSurvey.Application.Common;

public sealed class SurveyChangedException(SubmissionConflict conflict)
    : Exception("Ankieta zmieniła się w trakcie wypełniania.")
{
    public SubmissionConflict Conflict { get; } = conflict;
}
