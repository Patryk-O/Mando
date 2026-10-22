using System.Text.Json;
using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

/// <summary>
/// Zapis odpowiedzi wyłącznie przez procedurę dbo.usp_SaveSurveyResponse wywołaną z EF Core
/// (bez DbSet.Add / SaveChanges na encjach Answer).
/// </summary>
public sealed class SurveyResponseSaver(SurveyDbContext dbContext) : ISurveyResponseSaver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SubmitSurveyResponseResultDto> SaveAsync(
        SubmitSurveyResponseDto submission,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        var answersJson = JsonSerializer.Serialize(
            submission.Answers.Select(a => new
            {
                questionId = a.QuestionId,
                scaleValue = a.ScaleValue,
                boolValue = a.BoolValue,
                textValue = a.TextValue?.Trim()
            }),
            JsonOptions);

        try
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC dbo.usp_SaveSurveyResponse @SurveyId = {submission.SurveyId}, @SessionId = {sessionId}, @AnswersJson = {answersJson}",
                cancellationToken);
        }
        catch (Exception ex) when (MapConflict(ex) is SubmissionConflict conflict)
        {
            throw new SurveyChangedException(conflict);
        }

        return new SubmitSurveyResponseResultDto(sessionId);
    }

    private static SubmissionConflict? MapConflict(Exception exception) =>
        SurveyWriteSupport.SqlNumber(exception) switch
        {
            50001 => SubmissionConflict.SurveyClosed,
            50003 or 50004 => SubmissionConflict.FormChanged,
            _ => null
        };
}
