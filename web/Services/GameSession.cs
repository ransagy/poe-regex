using PoeRegex.Web.Core;

namespace web.Services;

/// <summary>Owns the active game, its engine data, and per-game workbench state.</summary>
public sealed class GameSession(
    EngineService engine, WorkbenchState state,
    PersistenceService persistence, GameDataLoader loader)
{
    public string Current { get; private set; } = "poe2";
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var game = await persistence.LoadLastGameAsync();
        if (game != "poe1") return;
        var switched = false;
        try
        {
            IsLoading = true;
            engine.LoadGame(await loader.LoadAsync("poe1"));
            Current = "poe1";
            switched = true;
        }
        catch (Exception ex)
        {
            Error = $"PoE1 data could not be loaded: {ex.Message}";
            Current = "poe2";
        }
        finally
        {
            IsLoading = false;
        }
        if (switched) Changed?.Invoke();
    }

    public WorkbenchSnapshot Capture() => WorkbenchSnapshot.Capture(state, Current);

    public Task SaveCurrentAsync() => persistence.SaveAsync(Capture(), Current);

    public async Task SwitchAsync(string game)
    {
        if (game is not ("poe1" or "poe2") || game == Current || IsLoading) return;
        Error = null;
        IsLoading = true;
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
            Error = $"{game} data could not be loaded: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Changed?.Invoke();
        }
    }
}
