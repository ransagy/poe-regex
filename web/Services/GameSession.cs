using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>Owns the active game, its engine data, and per-game workbench state.</summary>
public sealed class GameSession(
    EngineService engine, WorkbenchState state,
    PersistenceService persistence, GameDataLoader loader)
{
    public string Current { get; private set; } = "poe2";
    public bool IsReady { get; private set; }
    public bool IsLoading { get; private set; }

    /// <summary>The game being loaded while <see cref="IsLoading"/> is true.</summary>
    public string? LoadingGame { get; private set; }

    public string? Error { get; private set; }

    public event Action? Changed;

    /// <summary>Raised when a game load starts and after it finishes (success or failure).</summary>
    public event Action? LoadingChanged;

    public async Task InitializeAsync()
    {
        var game = await persistence.LoadLastGameAsync();
        if (game != "poe1")
        {
            IsReady = true;
            NotifyLoading();
            return;
        }
        var switched = false;
        try
        {
            IsLoading = true;
            LoadingGame = "poe1";
            NotifyLoading();
            engine.LoadGame(await loader.LoadAsync("poe1"));
            Current = "poe1";
            switched = true;
        }
        catch (Exception ex)
        {
            Error = $"Path of Exile data could not be loaded: {ex.Message}";
            Current = "poe2";
        }
        finally
        {
            IsLoading = false;
            LoadingGame = null;
            IsReady = true;
        }
        try
        {
            if (switched) Changed?.Invoke();
        }
        finally
        {
            NotifyLoading();
        }
    }

    public WorkbenchSnapshot Capture() => WorkbenchSnapshot.Capture(state, Current);

    public Task SaveCurrentAsync() => persistence.SaveAsync(Capture(), Current);

    public async Task SwitchAsync(string game)
    {
        if (game is not ("poe1" or "poe2") || game == Current || IsLoading) return;
        Error = null;
        IsLoading = true;
        LoadingGame = game;
        NotifyLoading();
        try
        {
            await persistence.SaveAsync(Capture(), Current);
            var data = await loader.LoadAsync(game);
            var saved = await persistence.LoadAsync(game);
            engine.LoadGame(data);
            Current = game;
            await persistence.SaveLastGameAsync(game);
            if (saved is null) state.Reset(game);
            else WorkbenchSnapshot.Restore(state, saved);
        }
        catch (Exception ex)
        {
            Error = $"{GameName(game)} data could not be loaded: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadingGame = null;
            try
            {
                Changed?.Invoke();
            }
            finally
            {
                NotifyLoading();
            }
        }
    }

    private void NotifyLoading()
    {
        try { LoadingChanged?.Invoke(); }
        catch { /* UI listeners must not strand the loading state */ }
    }

    private static string GameName(string game) =>
        game == "poe1" ? "Path of Exile" : "Path of Exile 2";
}
