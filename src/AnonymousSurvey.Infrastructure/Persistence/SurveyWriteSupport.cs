using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Entities;
using AnonymousSurvey.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AnonymousSurvey.Infrastructure.Persistence;

internal static class SurveyWriteSupport
{
    public const string SlugTaken = "Ten adres jest już zajęty.";
    public const string ActiveNeedsQuestion = "Ankieta aktywna musi mieć co najmniej jedno pytanie.";
    public const string ClosedNeedsQuestion = "Tylko szkic może nie mieć pytań.";
    public const string TooManyItems = "Ankieta może zawierać maksymalnie 30 pytań.";
    public const string InvalidToken = "Nieprawidłowy token konfiguracji.";
    public const string CannotUnsetDefault = "Aby zmienić ankietę domyślną, ustaw inną jako domyślną.";
    public const string ConcurrencyMessage = "Konfiguracja została zmieniona przez kogoś innego. Odśwież stronę.";
    public const string InvalidSlug = "Nieprawidłowy adres ankiety.";
    public const string TitleRequired = "Podaj tytuł ankiety.";
    public const string TitleTooLong = "Tytuł może mieć maksymalnie 200 znaków.";
    public const string DescriptionTooLong = "Opis może mieć maksymalnie 1000 znaków.";
    public const string QuestionRequired = "Tekst pytania jest wymagany.";
    public const string QuestionTooLong = "Tekst pytania może mieć maksymalnie 500 znaków.";
    public const string ItemShape = "Każda pozycja musi wskazywać istniejące pytanie albo nowe pytanie.";
    public const string DuplicateItem = "Pytanie jest wskazane więcej niż raz.";
    public const string InactiveQuestion = "Pytanie nie jest dostępne w puli.";
    public const string InvalidType = "Nieobsługiwany typ pytania.";
    public const string InvalidStatus = "Nieprawidłowy status ankiety.";
    public const string RetryLater = "Nie udało się zapisać ankiety. Spróbuj ponownie.";

    public static string PoolMessage(string text) =>
        $"Pytanie „{text}” już istnieje w puli — dodaj je z listy.";

    public static string UnknownQuestion(int id) => $"Nieznane pytanie (Id {id}).";

    public static NormalizedSurvey Normalize(string? title, string? description, SurveyStatus status)
    {
        var normalized = new NormalizedSurvey();
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0)
        {
            normalized.Errors.Add(TitleRequired);
        }
        else if (trimmedTitle.Length > SurveyLimits.MaxTitleLength)
        {
            normalized.Errors.Add(TitleTooLong);
        }

        normalized.Title = trimmedTitle;

        if (!Enum.IsDefined(status))
        {
            normalized.Errors.Add(InvalidStatus);
        }

        var descriptionText = description?.Trim();
        if (string.IsNullOrEmpty(descriptionText))
        {
            normalized.Description = null;
        }
        else if (descriptionText.Length > SurveyLimits.MaxDescriptionLength)
        {
            normalized.Errors.Add(DescriptionTooLong);
            normalized.Description = descriptionText;
        }
        else
        {
            normalized.Description = descriptionText;
        }

        return normalized;
    }

    public static async Task<ItemComposition> ComposeAsync(
        SurveyDbContext db,
        IReadOnlyList<SurveyItemInputDto>? items,
        SurveyStatus status,
        CancellationToken cancellationToken)
    {
        var result = new ItemComposition();
        var list = items ?? [];

        if (list.Count > SurveyLimits.MaxItemsPerSurvey)
        {
            result.Errors.Add(TooManyItems);
            return result;
        }

        if (list.Count == 0)
        {
            if (status == SurveyStatus.Active)
            {
                result.Errors.Add(ActiveNeedsQuestion);
            }
            else if (status != SurveyStatus.Draft)
            {
                result.Errors.Add(ClosedNeedsQuestion);
            }

            return result;
        }

        var seenIds = new HashSet<int>();
        var seenNew = new List<(QuestionType Type, string Text)>();

        foreach (var item in list)
        {
            var hasId = item.QuestionId is not null;
            var hasNew = item.NewQuestion is not null;
            if (hasId == hasNew)
            {
                result.Errors.Add(ItemShape);
                continue;
            }

            if (hasId)
            {
                var id = item.QuestionId!.Value;
                if (!seenIds.Add(id))
                {
                    result.Errors.Add(DuplicateItem);
                    continue;
                }

                result.Items.Add(new ResolvedItem(id, null, default, item.IsRequired));
                continue;
            }

            var text = item.NewQuestion!.Text?.Trim() ?? string.Empty;
            var type = item.NewQuestion.Type;
            if (text.Length == 0)
            {
                result.Errors.Add(QuestionRequired);
            }
            else if (text.Length > SurveyLimits.MaxQuestionTextLength)
            {
                result.Errors.Add(QuestionTooLong);
            }

            if (!Enum.IsDefined(type))
            {
                result.Errors.Add(InvalidType);
            }

            if (text.Length is < 1 or > SurveyLimits.MaxQuestionTextLength || !Enum.IsDefined(type))
            {
                continue;
            }

            if (seenNew.Any(existing => existing.Type == type && string.Equals(existing.Text, text, StringComparison.OrdinalIgnoreCase)))
            {
                result.Errors.Add(PoolMessage(text));
                continue;
            }

            seenNew.Add((type, text));
            result.Items.Add(new ResolvedItem(null, text, type, item.IsRequired));
        }

        if (result.Items.Count == 0)
        {
            return result;
        }

        var known = await db.Questions.AsNoTracking()
            .Select(q => new QuestionRef(q.Id, q.Text, q.Type, q.IsActive))
            .ToListAsync(cancellationToken);

        foreach (var id in result.Items.Where(item => item.QuestionId is not null).Select(item => item.QuestionId!.Value))
        {
            var question = known.FirstOrDefault(q => q.Id == id);
            if (question is null)
            {
                result.Errors.Add(UnknownQuestion(id));
            }
            else if (!question.IsActive)
            {
                result.Errors.Add(InactiveQuestion);
            }
        }

        foreach (var created in result.Items.Where(item => item.QuestionId is null))
        {
            var duplicate = known.Any(q =>
                q.Type == created.Type
                && string.Equals(q.Text.Trim(), created.NewText, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                result.Errors.Add(PoolMessage(created.NewText!));
            }
        }

        return result;
    }

    public static async Task<string> NextFreeSlugAsync(
        SurveyDbContext db,
        string baseSlug,
        CancellationToken cancellationToken)
    {
        var candidate = FitSlug(baseSlug, null);
        if (!await db.Surveys.AnyAsync(s => s.Slug == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            candidate = FitSlug(baseSlug, suffix);
            if (!SlugGenerator.IsValid(candidate))
            {
                continue;
            }

            if (!await db.Surveys.AnyAsync(s => s.Slug == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Brak wolnego adresu ankiety.");
    }

    public static string FitSlug(string baseSlug, int? suffix)
    {
        if (suffix is null)
        {
            var bare = baseSlug.Length <= SlugGenerator.MaxLength
                ? baseSlug
                : baseSlug[..SlugGenerator.MaxLength].TrimEnd('-');
            return bare.Length >= SlugGenerator.MinLength ? bare : "ankieta";
        }

        var extra = "-" + suffix.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var maxBase = Math.Max(1, SlugGenerator.MaxLength - extra.Length);
        var trimmed = baseSlug.Length <= maxBase ? baseSlug : baseSlug[..maxBase].TrimEnd('-');
        if (trimmed.Length == 0)
        {
            trimmed = "ankieta";
        }

        var result = trimmed + extra;
        return result.Length <= SlugGenerator.MaxLength
            ? result
            : result[..SlugGenerator.MaxLength].TrimEnd('-');
    }

    public static Task<Survey?> FindByConfigTokenAsync(
        SurveyDbContext db,
        string? token,
        bool track,
        CancellationToken cancellationToken) =>
        FindByTokenAsync(db, token, track, survey => survey.ConfigAccessToken, cancellationToken);

    public static Task<Survey?> FindByResultsTokenAsync(
        SurveyDbContext db,
        string? token,
        CancellationToken cancellationToken) =>
        FindByTokenAsync(db, token, track: false, survey => survey.ResultsAccessToken, cancellationToken);

    public static async Task<string> MatchingNewQuestionTextAsync(
        SurveyDbContext db,
        IReadOnlyList<ResolvedItem> items,
        CancellationToken cancellationToken)
    {
        var created = items.Where(item => item.QuestionId is null && item.NewText is not null).ToList();
        if (created.Count == 0)
        {
            return "pytanie";
        }

        var existing = await db.Questions.AsNoTracking()
            .Select(q => new { q.Text, q.Type })
            .ToListAsync(cancellationToken);

        foreach (var item in created)
        {
            if (existing.Any(q => q.Type == item.Type && string.Equals(q.Text.Trim(), item.NewText, StringComparison.OrdinalIgnoreCase)))
            {
                return item.NewText!;
            }
        }

        return created[0].NewText!;
    }

    public static bool IsUniqueIndex(Exception exception, string indexName)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SqlException sql || sql.Number is not (2601 or 2627))
            {
                continue;
            }

            foreach (SqlError error in sql.Errors)
            {
                if (error.Number is 2601 or 2627
                    && error.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 1205)
            {
                return true;
            }
        }

        return false;
    }

    public static int? SqlNumber(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql.Number;
            }
        }

        return null;
    }

    public static async Task RollbackQuietlyAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Transakcja mogła już zostać wycofana przez serwer (XACT_ABORT / deadlock).
        }
    }

    private static async Task<Survey?> FindByTokenAsync(
        SurveyDbContext db,
        string? token,
        bool track,
        Func<Survey, string> tokenOf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        IQueryable<Survey> query = track ? db.Surveys : db.Surveys.AsNoTracking();
        // Porównanie w SQL jest nieczułe na wielkość liter (collation CI). Dopasowanie Ordinal
        // odrzuca token różniący się tylko wielkością liter — unikalny indeks i tak nie trzyma dwóch wariantów.
        var matches = await query
            .Where(survey => survey.ConfigAccessToken == token || survey.ResultsAccessToken == token)
            .ToListAsync(cancellationToken);
        return matches.FirstOrDefault(survey => string.Equals(tokenOf(survey), token, StringComparison.Ordinal));
    }
}

internal sealed class NormalizedSurvey
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<string> Errors { get; } = [];
}

internal sealed class ItemComposition
{
    public List<string> Errors { get; } = [];

    public List<ResolvedItem> Items { get; } = [];
}

internal sealed record ResolvedItem(int? QuestionId, string? NewText, QuestionType Type, bool IsRequired);

internal sealed record QuestionRef(int Id, string Text, QuestionType Type, bool IsActive);
