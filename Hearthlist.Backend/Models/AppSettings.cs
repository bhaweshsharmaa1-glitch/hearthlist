namespace Hearthlist.Backend.Models;

public sealed class AppSettings
{
    public Companion SelectedCompanion { get; set; } = Companion.Shadow;
    public TileThemeVariant TileTheme { get; set; } = TileThemeVariant.Violet;
    public int SleepyTimeoutMinutes { get; set; } = 5;
    public bool LockScreenEnabled { get; set; } = false;
}
