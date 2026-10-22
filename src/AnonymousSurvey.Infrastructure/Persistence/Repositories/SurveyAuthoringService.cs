using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Entities;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence.Repositories;

public sealed class SurveyAuthoringService(SurveyDbContext dbContext) : ISurveyAuthoringService
{
    private const int MaxAttempts = 5;

    public async Task<IReadOnlyList<QuestionPoolItemDto>> GetQuestionPoolAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Questions
            .AsNoTracking()
            .Where(question => question.IsActive)
            .OrderBy(question => question.Id)
            .Select(question => new QuestionPoolItemDto(question.Id, question.Text, question.Type, question.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<CreateSurveyOutcome> CreateAsync(CreateSurveyDto survey, CancellationToken cancellationToken = default)
    {
        var metadata = SurveyWriteSupport.Normalize(survey.Title, survey.Description, survey.Status);
        var composition = await SurveyWriteSupport.ComposeAsync(dbContext, survey.Items, survey.Status, cancellationToken);

        var manualSlug = !string.IsNullOrWhiteSpace(survey.Slug);
        var requestedSlug = manualSlug ? survey.Slug!.Trim() : null;
        if (manualSlug && !SlugGenerator.IsValid(requestedSlug!))
        {
            metadata.Errors.Add(SurveyWriteSupport.InvalidSlug);
        }

        var errors = metadata.Errors.Concat(composition.Errors).ToList();
        if (errors.Count > 0 || !Enum.IsDefined(survey.Status))
        {
            return Failure(errors);
        }

        var baseSlug = manualSlug ? requestedSlug! : SlugGenerator.FromTitle(metadata.Title);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (manualSlug && await dbContext.Surveys.AnyAsync(existing => existing.Slug == baseSlug, cancellationToken))
                {
                    await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                    return Failure(SurveyWriteSupport.SlugTaken);
                }

                var slug = manualSlug
                    ? baseSlug
                    : await SurveyWriteSupport.NextFreeSlugAsync(dbContext, baseSlug, cancellationToken);
                var now = DateTime.UtcNow;
                var entity = new Survey
                {
                    Slug = slug,
                    Title = metadata.Title,
                    Description = metadata.Description,
                    Status = survey.Status,
                    IsDefault = !await dbContext.Surveys.AnyAsync(cancellationToken),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    ResultsAccessToken = AccessTokenGenerator.Results(),
                    ConfigAccessToken = AccessTokenGenerator.Config()
                };

                AddItems(entity, composition.Items, now);
                dbContext.Surveys.Add(entity);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new CreateSurveyOutcome(
                    ValidationResult.Success(),
                    new CreateSurveyResultDto(entity.Id, entity.Slug, entity.ResultsAccessToken, entity.ConfigAccessToken));
            }
            catch (DbUpdateException ex) when (SurveyWriteSupport.IsUniqueIndex(ex, "UX_Survey_Slug"))
            {
                await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                dbContext.ChangeTracker.Clear();
                if (manualSlug || attempt == MaxAttempts)
                {
                    return Failure(SurveyWriteSupport.SlugTaken);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
            catch (DbUpdateException ex) when (SurveyWriteSupport.IsUniqueIndex(ex, "UX_Question_Type_Text"))
            {
                await SurveyWriteSupport.RollbackQuietlyAsync(transaction, cancellationToken);
                dbContext.ChangeTracker.Clear();
                var text = await SurveyWriteSupport.MatchingNewQuestionTextAsync(dbContext, composition.Items, cancellationToken);
                return Failure(SurveyWriteSupport.PoolMessage(text));
            }
            catch (DbUpdateException ex) when (IsRetryableCreate(ex))
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

    private static bool IsRetryableCreate(Exception exception) =>
        SurveyWriteSupport.IsDeadlock(exception)
        || SurveyWriteSupport.IsUniqueIndex(exception, "UX_Survey_IsDefault")
        || SurveyWriteSupport.IsUniqueIndex(exception, "UX_Survey_ResultsAccessToken")
        || SurveyWriteSupport.IsUniqueIndex(exception, "UX_Survey_ConfigAccessToken");

    private static void AddItems(Survey survey, IReadOnlyList<ResolvedItem> items, DateTime now)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var link = new SurveyQuestion
            {
                SortOrder = index + 1,
                IsRequired = item.IsRequired,
                AddedAtUtc = now
            };

            if (item.QuestionId is int questionId)
            {
                link.QuestionId = questionId;
            }
            else
            {
                link.Question = new Question
                {
                    Text = item.NewText!,
                    Type = item.Type,
                    IsActive = true
                };
            }

            survey.SurveyQuestions.Add(link);
        }
    }

    private static CreateSurveyOutcome Failure(IEnumerable<string> errors) =>
        new(ValidationResult.Failure(errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray()), null);

    private static CreateSurveyOutcome Failure(string error) =>
        new(ValidationResult.Failure(error), null);
}
