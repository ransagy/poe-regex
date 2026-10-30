namespace web.Services;

/// <summary>
/// Shared first-run intro visibility. The page renders the inline card and the
/// header owns the "What is this?" opener, so both need the same state.
/// </summary>
public sealed class IntroState(PersistenceService persistence)
{
    private Task? _initialization;

    public bool? Visible { get; private set; }

    public event Action? Changed;

    public Task InitializeAsync() => _initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        Visible = !await persistence.LoadIntroDismissedAsync();
        Changed?.Invoke();
    }

    public void Show()
    {
        if (Visible == true) return;
        Visible = true;
        Changed?.Invoke();
    }

    public async Task DismissAsync()
    {
        Visible = false;
        Changed?.Invoke();
        await persistence.SaveIntroDismissedAsync();
    }
}
