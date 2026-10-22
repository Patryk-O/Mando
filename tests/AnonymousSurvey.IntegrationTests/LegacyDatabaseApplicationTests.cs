using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AnonymousSurvey.Application.Abstractions;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace AnonymousSurvey.IntegrationTests;

/// <summary>
/// Stara baza ze schematu 9334b2f (z seedem i odpowiedzią) przechodzi przez
/// <c>DatabaseInitializer</c> przy starcie aplikacji, a potem katalog, formularz i zapis działają.
/// </summary>
public class LegacyDatabaseApplicationTests
{
    [Fact]
    public async Task Upgraded_legacy_seed_starts_the_application_and_serves_the_survey()
    {
        await using var database = await SqlTestDatabase.CreateLegacyAsync(withLegacySeed: true);
        var session = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            INSERT INTO dbo.ResponseSession (Id, SurveyId, SubmittedAtUtc) VALUES (@id, 1, '2024-01-15T12:00:00');
            INSERT INTO dbo.Answer (SessionId, QuestionId, ScaleValue) VALUES (@id, 1, 4);
            INSERT INTO dbo.Answer (SessionId, QuestionId, ScaleValue) VALUES (@id, 2, 5);
            INSERT INTO dbo.Answer (SessionId, QuestionId, BoolValue) VALUES (@id, 3, 1);
            INSERT INTO dbo.Answer (SessionId, QuestionId, BoolValue) VALUES (@id, 4, 0);
            INSERT INTO dbo.Answer (SessionId, QuestionId, TextValue) VALUES (@id, 5, N'Zostaje po upgrade');
            """,
            new SqlParameter("@id", session));

        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var log = new StringBuilder();
        using var process = StartApplication(database.ConnectionString, baseUrl, log);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            await WaitUntilReadyAsync(client, process, baseUrl, log);

            var home = await client.GetAsync(baseUrl + "/");
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
            Assert.Contains("blazor", await home.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            var surveyPage = await client.GetAsync(baseUrl + "/ankieta/ankieta-1");
            Assert.Equal(HttpStatusCode.OK, surveyPage.StatusCode);

            var resultsPage = await client.GetAsync(baseUrl + "/wyniki/wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13");
            Assert.Equal(HttpStatusCode.OK, resultsPage.StatusCode);

            await using var services = database.BuildServices();
            await using var scope = services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var catalog = provider.GetRequiredService<ISurveyCatalogQuery>();
            Assert.Equal("ankieta-1", await catalog.GetDefaultSlugAsync());

            var survey = await catalog.GetBySlugAsync("ankieta-1");
            Assert.NotNull(survey);
            Assert.Equal("Ankieta satysfakcji", survey.Title);
            Assert.Equal(SurveyStatus.Active, survey.Status);

            var questions = await provider.GetRequiredService<ISurveyQuestionQuery>().GetQuestionsAsync(survey.SurveyId);
            Assert.Equal(5, questions.Count);
            Assert.All(questions, question => Assert.True(question.IsRequired));

            var results = await provider.GetRequiredService<ISurveyResultsQuery>()
                .GetByResultsTokenAsync("wyniki-4f9c2a7e1b3d48a69c5e0d7f2a8b6c13");
            Assert.NotNull(results);
            Assert.Equal(1, results.TotalSessions);
            Assert.Contains(results.OpenAggregates, item => item.Text.Contains("lepiej", StringComparison.Ordinal));
            Assert.Contains(
                results.RawResponses,
                row => row.Answers.Any(answer => answer.TextValue == "Zostaje po upgrade"));

            var submitted = await provider.GetRequiredService<ISurveySubmissionService>().SubmitAsync(
                new SubmitSurveyResponseDto(survey.SurveyId, questions.Select(ToAnswer).ToList()));
            Assert.True(submitted.Succeeded, string.Join("; ", submitted.Validation.Errors));
        }
        finally
        {
            Stop(process);
        }

        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Survey') AND name = N'IsActive'"));
        Assert.Equal(2, await database.CountAsync("ResponseSession"));
        Assert.Equal(1, await database.CountAsync("Answer", $"SessionId = '{session}' AND TextValue = N'Zostaje po upgrade'"));
    }

    private static AnswerInputDto ToAnswer(SurveyQuestionDto question) => question.Type switch
    {
        QuestionType.Scale => new AnswerInputDto(question.QuestionId, 3, null, null),
        QuestionType.YesNo => new AnswerInputDto(question.QuestionId, null, false, null),
        QuestionType.Open => new AnswerInputDto(question.QuestionId, null, null, "Nowa po upgrade"),
        _ => throw new InvalidOperationException($"Nieobsługiwany typ {question.Type}.")
    };

    private static Process StartApplication(string connectionString, string baseUrl, StringBuilder log)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(Path.Combine("src", "AnonymousSurvey.Web"));
        start.ArgumentList.Add("--no-launch-profile");
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(baseUrl);
        start.Environment["ConnectionStrings__SurveyDb"] = connectionString;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "UpgradeTest";
        start.Environment["DOTNET_NOLOGO"] = "1";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => Append(log, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => Append(log, eventArgs.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("Nie udało się uruchomić aplikacji.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilReadyAsync(HttpClient client, Process process, string baseUrl, StringBuilder log)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Aplikacja zakończyła się kodem {process.ExitCode} przed gotowością.{Environment.NewLine}{Tail(log)}");
            }

            try
            {
                var response = await client.GetAsync(baseUrl + "/");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                last = exception;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Aplikacja nie odpowiedziała na GET /.{Environment.NewLine}{last}{Environment.NewLine}{Tail(log)}");
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(15_000);
            }
        }
        catch (Exception)
        {
            // Proces mógł już zniknąć. Nie maskujemy wyniku testu.
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AnonymousSurvey.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Nie znaleziono AnonymousSurvey.sln.");
    }

    private static void Append(StringBuilder log, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (log)
        {
            log.AppendLine(line);
        }
    }

    private static string Tail(StringBuilder log)
    {
        lock (log)
        {
            var text = log.ToString();
            return text.Length <= 4000 ? text : text[^4000..];
        }
    }
}
