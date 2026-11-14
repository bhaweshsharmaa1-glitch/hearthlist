using System.Text.Json;
using Hearthlist.Backend.Models;

namespace Hearthlist.Backend.Services;

/// <summary>
/// Manages the selected companion (which defines the theme/realm) and
/// persists the choice + app settings via <see cref="StorageProvider"/>.
/// </summary>
public class ThemeManager
{
    private const string SettingsFileName = "settings.json";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private AppSettings _settings;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public event EventHandler<Companion>? CompanionChanged;

    public AppSettings Settings => _settings;

    public Companion CurrentCompanion => _settings.SelectedCompanion;

    public ThemeColors CurrentPalette => CreaturePalettes.Get(CurrentCompanion);

    public TileThemeVariant CurrentTileTheme => _settings.TileTheme;

    public ThemeManager()
    {
        _settings = new AppSettings();
    }

    public async Task InitializeAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetFileIfExistsAsync(SettingsFileName);
            var raw = handle is null ? string.Empty : await StorageProvider.ReadTextAsync(handle);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(raw, _jsonOptions);
                if (loaded is not null)
                    _settings = loaded;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SetCompanionAsync(Companion companion)
    {
        _settings.SelectedCompanion = companion;
        await SaveAsync();
        CompanionChanged?.Invoke(this, companion);
    }

    public async Task SetTileThemeAsync(TileThemeVariant theme)
    {
        if (!Enum.IsDefined(theme))
            throw new ArgumentOutOfRangeException(nameof(theme));

        _settings.TileTheme = theme;
        await SaveAsync();
    }

    public async Task SetSleepyTimeoutAsync(int minutes)
    {
        if (minutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Must be positive.");

        _settings.SleepyTimeoutMinutes = minutes;
        await SaveAsync();
    }

    public async Task SetLockScreenEnabledAsync(bool enabled)
    {
        _settings.LockScreenEnabled = enabled;
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetOrCreateFileAsync(SettingsFileName);
            await StorageProvider.WriteTextAsync(handle, JsonSerializer.Serialize(_settings, _jsonOptions));
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
