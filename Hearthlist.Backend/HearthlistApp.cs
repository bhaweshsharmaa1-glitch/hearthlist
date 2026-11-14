using Hearthlist.Backend.Models;
using Hearthlist.Backend.Services;

namespace Hearthlist.Backend;

/// <summary>
/// Single entry point for the renderer (or any host app).
/// Wires together TodoService, ThemeManager, CompanionStateMachine and
/// LockScreenService, exposing observable state the renderer can consume.
/// </summary>
public class HearthlistApp : IAsyncDisposable
{
    private readonly LockScreenService _lockScreenService;

    public TodoService Todos { get; }
    public TileService Tiles { get; }
    public ThemeManager Themes { get; }
    public CompanionStateMachine Companion { get; }

    public HearthlistApp()
    {
        Todos = new TodoService();
        Tiles = new TileService();
        Themes = new ThemeManager();
        Companion = new CompanionStateMachine(
            TimeSpan.FromMinutes(Themes.Settings.SleepyTimeoutMinutes));

        _lockScreenService = new LockScreenService(Todos, Themes);

        // Refresh the (static, lock-screen) snapshot whenever tasks change.
        Todos.TasksChanged += (_, _) => _ = OnTasksChangedSafely();
    }

    /// <summary>
    /// Load persisted data and set initial companion state. Call once at startup.
    /// </summary>
    public async Task StartAsync()
    {
        await Themes.InitializeAsync();
        await Todos.InitializeAsync();
        await Tiles.InitializeAsync();

        // Keep the state machine's timer in sync with persisted settings.
        Companion.SetSleepyTimeout(TimeSpan.FromMinutes(Themes.Settings.SleepyTimeoutMinutes));

        Companion.OnAppOpened();
    }

    /// <summary>Example helper: mark a task complete AND reflect it in companion mood.</summary>
    public async Task<bool> CompleteTaskAsync(string id)
    {
        var changed = await Todos.SetCompletedAsync(id, true);
        if (changed)
            Companion.TriggerHappy();
        return changed;
    }

    public async Task UncompleteTaskAsync(string id)
    {
        await Todos.SetCompletedAsync(id, false);
    }

    private async Task OnTasksChangedSafely()
    {
        try
        {
            // Only spend resources generating the snapshot when the feature is switched on.
            // In v1 the flag is OFF, so this is a no-op (feature dormant, no per-change PNG work).
            await _lockScreenService.GenerateAndApplyAsync();
        }
        catch (Exception ex)
        {
            // Never let a background refresh crash the app; surface via a debug trace.
            System.Diagnostics.Debug.WriteLine($"[Hearthlist] Lock screen refresh failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Companion.Dispose();
        await ValueTask.CompletedTask;
    }
}
