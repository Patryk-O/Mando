using Microsoft.AspNetCore.Components;

namespace AnonymousSurvey.Web.Components.Pages;

/// <summary>
/// Bazowa strona: wspólny cykl ładowania (loading / błąd / nie znaleziono). Strony potomne
/// (formularz, podziękowanie, wyniki, konfiguracja) nadpisują tytuł oraz sposób ładowania danych.
/// </summary>
public abstract class BasePage : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    [Inject] protected NavigationManager Navigation { get; set; } = null!;

    [Inject] protected ILogger<BasePage> Logger { get; set; } = null!;

    protected abstract string PageTitle { get; }

    protected bool IsLoading { get; private set; } = true;

    protected bool IsNotFound { get; set; }

    protected string? LoadError { get; private set; }

    protected CancellationToken PageCancellation => _cts.Token;

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    protected async Task ReloadAsync()
    {
        IsLoading = true;
        IsNotFound = false;
        LoadError = null;

        try
        {
            await LoadAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Nie udało się załadować strony {Page}.", PageTitle);
            LoadError = "Nie udało się wczytać danych. Spróbuj ponownie za chwilę.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public virtual void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
