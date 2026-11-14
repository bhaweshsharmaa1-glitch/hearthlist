using System.Windows;
using Hearthlist.Backend.Models;

namespace Hearthlist.Desktop;

public partial class App : Application
{
    private Hearthlist.Backend.HearthlistApp? _backend;

    public Hearthlist.Backend.HearthlistApp Backend => _backend!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // No StartupUri in XAML — we init the backend first, then show the window.
        _backend = new Hearthlist.Backend.HearthlistApp();
        await _backend.StartAsync();
        ApplyTileTheme(_backend.Themes.CurrentTileTheme);

        var window = new NoteListWindow(this);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backend?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    public async Task SetTileThemeAsync(TileThemeVariant theme)
    {
        await Backend.Themes.SetTileThemeAsync(theme);
        ApplyTileTheme(theme);
    }

    private void ApplyTileTheme(TileThemeVariant theme)
    {
        if (!Enum.IsDefined(theme))
            theme = TileThemeVariant.Violet;

        var dictionaries = Resources.MergedDictionaries;
        var previous = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("TileTheme.", StringComparison.Ordinal) == true);
        if (previous is not null)
            dictionaries.Remove(previous);

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Hearthlist.Desktop;component/Themes/TileTheme.{theme}.xaml", UriKind.Relative)
        });
    }

    public void ApplyTileTheme(FrameworkElement element, TileThemeVariant theme)
    {
        if (!Enum.IsDefined(theme))
            theme = TileThemeVariant.Slate;

        element.Resources.MergedDictionaries.Clear();
        element.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/Hearthlist.Desktop;component/Themes/TileTheme.{theme}.xaml", UriKind.Relative)
        });
    }
}
