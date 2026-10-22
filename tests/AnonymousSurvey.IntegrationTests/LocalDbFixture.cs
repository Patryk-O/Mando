using AnonymousSurvey.Domain.Entities;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// Fixture bazy na klasę testową: <c>public class MyTests(LocalDbFixture fixture) : IClassFixture&lt;LocalDbFixture&gt;</c>.
/// Każda klasa dostaje własną, świeżą bazę <c>AnonymousSurvey_Test_&lt;guid&gt;</c> (z seedem), usuwaną po klasie.
/// Testy nie powinny polegać na seedzie — dane tworzą przez <see cref="CreateSurveyAsync"/>.
/// </summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    public const string ResultsToken = "wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13";
    public const string ConfigToken = "konfiguracja-8e1d5b3a9c7f42d6b0a4e2c9f7d1b538";

    private TempDatabase? _database;

    public TempDatabase Database => _database ?? throw new InvalidOperationException("Fixture nie została zainicjalizowana.");

    public ServiceProvider Services => Database.Services;

    public string DatabaseName => Database.Name;

    public string ConnectionString => Database.ConnectionString;

    public async Task InitializeAsync() => _database = await TempDatabase.CreateAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    public AsyncServiceScope NewScope() => Services.CreateAsyncScope();

    /// <summary>
    /// Tworzy ankietę przez EF z unikalnym slugiem (<c>test-&lt;guid&gt;</c>), własnymi tokenami i własnymi,
    /// unikalnymi pytaniami (nie dotyka seeda ani innych ankiet; wyjątek: <paramref name="isDefault"/> zdejmuje flagę domyślnej z pozostałych).
    /// Domyślnie: Aktywna, 3 pytania wymagane (skala, Tak/Nie, otwarte).
    /// </summary>
    public async Task<CreatedSurvey> CreateSurveyAsync(
        SurveyStatus status = SurveyStatus.Active,
        IReadOnlyList<TestQuestion>? questions = null,
        string? title = null,
        string? description = null,
        bool isDefault = false)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var specs = questions ?? TestQuestion.Default;

        await using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();

        if (isDefault)
        {
            await db.Surveys.Where(s => s.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        }

        var survey = new Survey
        {
            Slug = $"test-{id}",
            Title = title ?? $"Ankieta testowa {id[..8]}",
            Description = description,
            Status = status,
            IsDefault = isDefault,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ResultsAccessToken = $"wyniki-{id}",
            ConfigAccessToken = $"konfiguracja-{id}"
        };

        var created = new List<Question>();
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var question = new Question
            {
                Text = spec.Text ?? $"Pytanie testowe {i + 1} [{spec.Type}] {id[..12]}",
                Type = spec.Type,
                IsActive = true
            };
            created.Add(question);
            survey.SurveyQuestions.Add(new SurveyQuestion
            {
                Question = question,
                SortOrder = i + 1,
                IsRequired = spec.IsRequired,
                AddedAtUtc = now
            });
        }

        db.Surveys.Add(survey);
        await db.SaveChangesAsync();

        return new CreatedSurvey(
            survey.Id,
            survey.Slug,
            survey.Title,
            survey.ResultsAccessToken,
            survey.ConfigAccessToken,
            created.Select(q => q.Id).ToList());
    }
}

public sealed record TestQuestion(QuestionType Type, bool IsRequired = true, string? Text = null)
{
    public static IReadOnlyList<TestQuestion> Default { get; } =
    [
        new(QuestionType.Scale),
        new(QuestionType.YesNo),
        new(QuestionType.Open)
    ];
}

public sealed record CreatedSurvey(
    int Id,
    string Slug,
    string Title,
    string ResultsToken,
    string ConfigToken,
    IReadOnlyList<int> QuestionIds);
