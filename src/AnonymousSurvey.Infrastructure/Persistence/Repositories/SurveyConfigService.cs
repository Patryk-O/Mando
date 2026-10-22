using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Entities;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyConfigService(SurveyDbContext dbContext) : ISurveyConfigService
{
    private const int MaxAttempts = 5;

    public async Task<SurveyConfigDto?> GetByConfigTokenAsync(
        string configAccessToken,
        CancellationToken cancellationToken = default)
    {
        var survey = await SurveyWriteSupport.FindByConfigTokenAsync(
            dbContext,
            configAccessToken,
            track: false,
            cancellationToken);

        if (survey is null)
        {
            return null;
        }

        var answerCounts = await dbContext.Answers
            .AsNoTracking()
            .Where(answer => answer.Session.SurveyId == survey.Id)
            .GroupBy(answer => answer.QuestionId)
            .Select(group => new { QuestionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.QuestionId, row => row.Count, cancellationToken);

        var assigned = (await dbContext.SurveyQuestions
                .AsNoTracking()
                .Where(link => link.SurveyId == survey.Id)
                .OrderBy(link => link.SortOrder)
                .Select(link => new { link.QuestionId, link.Question.Text, link.Question.Type, link.SortOrder, link.IsRequired })
                .ToListAsync(cancellationToken))
            .Select(link => new SurveyConfigQuestionDto(
                link.QuestionId,
                link.Text,
                link.Type,
                link.SortOrder,
                link.IsRequired,
                answerCounts.GetValueOrDefault(link.QuestionId)))
            .ToList();

        var pool = await dbContext.Questions
            .AsNoTracking()
            .Where(question => question.IsActive)
            .OrderBy(question => question.Id)
            .Select(question => new QuestionPoolItemDto(question.Id, question.Text, question.Type, question.IsActive))
            .ToListAsync(cancellationToken);

        var responseCount = await dbContext.ResponseSessions
            .AsNoTracking()
            .CountAsync(session => session.SurveyId == survey.Id, cancellationToken);

        return new SurveyConfigDto(
            survey.Id,
            survey.Slug,
            survey.Title,
            survey.Description,
            survey.Status,
            survey.IsDefault,
            responseCount,
            survey.RowVersion,
            assigned,
            pool);
    }

    public async Task<UpdateSurveyConfigOutcome> UpdateAsync(
        string configAccessToken,
        UpdateSurveyConfigDto update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configAccessToken))
        {
            return Failure(SurveyWriteSupport.InvalidToken);
        }

        var current = await SurveyWriteSupport.FindByConfigTokenAsync(
            dbContext,
            configAccessToken,
            track: false,
            cancellationToken);
        if (current is null || current.Id != update.SurveyId)
        {
            return Failure(SurveyWriteSupport.InvalidToken);
        }

        var metadata = SurveyWriteSupport.Normalize(update.Title, update.Description, update.Status);
        var composition = await SurveyWriteSupport.ComposeAsync(dbContext, update.Items, update.Status, cancellationToken);
        var errors = metadata.Errors.Concat(composition.Errors).ToList();
        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        if (update.RowVersion is not { Length: 8 })
        {
            return Concurrency();
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var survey = await SurveyWriteSupport.FindByConfigTokenAsync(
                    dbContext,
                    configAccessToken,
                    track: true,
                    cancellationToken);
                if (survey is null || survey.Id != update.SurveyId)
                {
                    await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                    return Failure(SurveyWriteSupport.InvalidToken);
                }

                if (survey.IsDefault && !update.IsDefault)
                {
                    await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                    return Failure(SurveyWriteSupport.CannotUnsetDefault);
                }

                if (update.IsDefault && !survey.IsDefault)
                {
                    await dbContext.Surveys
                        .Where(other => other.IsDefault && other.Id != survey.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(other => other.IsDefault, false), cancellationToken);
                    survey.IsDefault = true;
                }

                survey.Title = metadata.Title;
                survey.Description = metadata.Description;
                survey.Status = update.Status;
                survey.UpdatedAtUtc = DateTime.UtcNow;

                var entry = dbContext.Entry(survey);
                entry.Property(item => item.UpdatedAtUtc).IsModified = true;
                entry.Property(item => item.RowVersion).OriginalValue = update.RowVersion;

                await SyncAssignmentsAsync(survey.Id, composition.Items, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new UpdateSurveyConfigOutcome(ValidationResult.Success());
            }
            catch (DbUpdateConcurrencyException)
            {
                await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                dbContext.ChangeTracker.Clear();
                return Concurrency();
            }
            catch (DbUpdateException ex) when (SurveyWriteSupport.IsUniqueIndex(ex, "UX_Question_Type_Text"))
            {
                await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                dbContext.ChangeTracker.Clear();
                var text = await SurveyWriteSupport.MatchingNewQuestionTextAsync(dbContext, composition.Items, cancellationToken);
                return Failure(SurveyWriteSupport.PoolMessage(text));
            }
            catch (DbUpdateException ex) when (SurveyWriteSupport.IsDeadlock(ex)
                                               || SurveyWriteSupport.IsUniqueIndex(ex, "UX_Survey_IsDefault"))
            {
                await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                dbContext.ChangeTracker.Clear();
                if (attempt == MaxAttempts)
                {
                    return Failure(SurveyWriteSupport.RetryLater);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }

        return Failure(SurveyWriteSupport.RetryLater);
    }

    private async Task SyncAssignmentsAsync(
        int surveyId,
        IReadOnlyList<ResolvedItem> items,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SurveyQuestions
            .Where(link => link.SurveyId == surveyId)
            .ToListAsync(cancellationToken);
        var keptIds = new HashSet<int>();
        var now = DateTime.UtcNow;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var sortOrder = index + 1;
            if (item.QuestionId is int questionId)
            {
                keptIds.Add(questionId);
                var link = existing.FirstOrDefault(candidate => candidate.QuestionId == questionId);
                if (link is null)
                {
                    dbContext.SurveyQuestions.Add(new SurveyQuestion
                    {
                        SurveyId = surveyId,
                        QuestionId = questionId,
                        SortOrder = sortOrder,
                        IsRequired = item.IsRequired,
                        AddedAtUtc = now
                    });
                }
                else
                {
                    link.SortOrder = sortOrder;
                    link.IsRequired = item.IsRequired;
                }

                continue;
            }

            dbContext.SurveyQuestions.Add(new SurveyQuestion
            {
                SurveyId = surveyId,
                Question = new Question
                {
                    Text = item.NewText!,
                    Type = item.Type,
                    IsActive = true
                },
                SortOrder = sortOrder,
                IsRequired = item.IsRequired,
                AddedAtUtc = now
            });
        }

        foreach (var removed in existing.Where(link => !keptIds.Contains(link.QuestionId)))
        {
            dbContext.SurveyQuestions.Remove(removed);
        }
    }

    private static UpdateSurveyConfigOutcome Failure(string error) =>
        new(ValidationResult.Failure(error));

    private static UpdateSurveyConfigOutcome Failure(IEnumerable<string> errors) =>
        new(ValidationResult.Failure(errors.ToArray()));

    private static UpdateSurveyConfigOutcome Concurrency() =>
        new(ValidationResult.Failure(SurveyWriteSupport.ConcurrencyMessage), IsConcurrencyConflict: true);
}
