using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthlist.Backend.Models;

namespace Hearthlist.Backend.Services;

public sealed class TileService
{
    private const string TilesFileName = "tiles.json";
    private const int CurrentSchemaVersion = 2;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private List<Tile> _tiles = new();
    private TileMovementBounds _movementBounds = new();
    private readonly object _listLock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public IReadOnlyList<Tile> Tiles
    {
        get
        {
            lock (_listLock)
            {
                return _tiles.Select(Clone).ToList();
            }
        }
    }

    public TileMovementBounds MovementBounds
    {
        get
        {
            lock (_listLock)
            {
                return Clone(_movementBounds);
            }
        }
    }

    public event EventHandler<IReadOnlyList<Tile>>? TilesChanged;

    public async Task InitializeAsync()
    {
        TileStore store;
        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetFileIfExistsAsync(TilesFileName);
            var raw = handle is null ? string.Empty : await StorageProvider.ReadTextAsync(handle);
            store = string.IsNullOrWhiteSpace(raw)
                ? new TileStore()
                : JsonSerializer.Deserialize<TileStore>(raw, _jsonOptions) ?? new TileStore();
        }
        finally
        {
            _fileLock.Release();
        }

        lock (_listLock)
        {
            _tiles = store.Tiles ?? new List<Tile>();
            _movementBounds = store.MovementBounds ?? new TileMovementBounds();

            if (store.SchemaVersion < CurrentSchemaVersion)
            {
                for (var index = 0; index < _tiles.Count; index++)
                {
                    var offset = index % 8;
                    _tiles[index].Left = _movementBounds.Left + 32 + offset * 30;
                    _tiles[index].Top = _movementBounds.Top + 32 + offset * 26;
                }
            }
        }

        if (store.SchemaVersion < CurrentSchemaVersion)
            await SaveAndNotifyAsync();
    }

    public async Task<Tile> AddAsync(
        string title,
        string text = "",
        TileKind kind = TileKind.Note,
        double left = 120,
        double top = 120,
        TileThemeVariant theme = TileThemeVariant.Slate)
    {
        var tile = new Tile
        {
            Kind = kind,
            Title = title,
            Text = text,
            Left = left,
            Top = top,
            Theme = theme
        };

        lock (_listLock)
        {
            _tiles.Add(tile);
        }

        await SaveAndNotifyAsync();
        return Clone(tile);
    }

    public async Task<bool> UpdateTextAsync(string id, string text)
    {
        lock (_listLock)
        {
            var tile = _tiles.FirstOrDefault(candidate => candidate.Id == id);
            if (tile is null)
                return false;

            tile.Text = text;
            tile.UpdatedAt = DateTime.UtcNow;
        }

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task<bool> UpdatePositionAsync(string id, double left, double top)
    {
        lock (_listLock)
        {
            var tile = _tiles.FirstOrDefault(candidate => candidate.Id == id);
            if (tile is null)
                return false;

            tile.Left = left;
            tile.Top = top;
            tile.UpdatedAt = DateTime.UtcNow;
        }

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task UpdatePositionsAsync(IReadOnlyDictionary<string, (double Left, double Top)> positions)
    {
        var changed = false;
        lock (_listLock)
        {
            foreach (var tile in _tiles)
            {
                if (!positions.TryGetValue(tile.Id, out var position) ||
                    (tile.Left == position.Left && tile.Top == position.Top))
                {
                    continue;
                }

                tile.Left = position.Left;
                tile.Top = position.Top;
                tile.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
            await SaveAndNotifyAsync();
    }

    public async Task<bool> UpdateSettingsAsync(
        string id,
        string? title = null,
        double? width = null,
        double? height = null,
        bool? isDraggable = null,
        TileThemeVariant? theme = null,
        TileBorderStyle? borderStyle = null)
    {
        lock (_listLock)
        {
            var tile = _tiles.FirstOrDefault(candidate => candidate.Id == id);
            if (tile is null)
                return false;

            if (!string.IsNullOrWhiteSpace(title))
                tile.Title = title.Trim();
            if (width is not null)
                tile.Width = Math.Clamp(width.Value, 140, 900);
            if (height is not null)
                tile.Height = Math.Clamp(height.Value, 90, 700);
            if (isDraggable is not null)
                tile.IsDraggable = isDraggable.Value;
            if (theme is not null)
            {
                if (!Enum.IsDefined(theme.Value))
                    throw new ArgumentOutOfRangeException(nameof(theme));
                tile.Theme = theme.Value;
            }
            if (borderStyle is not null)
            {
                if (!Enum.IsDefined(borderStyle.Value))
                    throw new ArgumentOutOfRangeException(nameof(borderStyle));
                tile.BorderStyle = borderStyle.Value;
            }
            tile.UpdatedAt = DateTime.UtcNow;
        }

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task UpdateMovementBoundsAsync(TileMovementBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.Width < 200 || bounds.Height < 150)
            throw new ArgumentException("The movement area must be at least 200 by 150 pixels.", nameof(bounds));

        lock (_listLock)
        {
            _movementBounds = Clone(bounds);
        }

        await SaveAndNotifyAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        lock (_listLock)
        {
            var tile = _tiles.FirstOrDefault(candidate => candidate.Id == id);
            if (tile is null)
                return false;

            _tiles.Remove(tile);
        }

        await SaveAndNotifyAsync();
        return true;
    }

    private async Task SaveAndNotifyAsync()
    {
        string json;
        lock (_listLock)
        {
            var store = new TileStore
            {
                SchemaVersion = CurrentSchemaVersion,
                Tiles = _tiles.Select(Clone).ToList(),
                MovementBounds = Clone(_movementBounds)
            };
            json = JsonSerializer.Serialize(store, _jsonOptions);
        }

        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetOrCreateFileAsync(TilesFileName);
            await StorageProvider.WriteTextAsync(handle, json);
        }
        finally
        {
            _fileLock.Release();
        }

        TilesChanged?.Invoke(this, Tiles);
    }

    private static Tile Clone(Tile tile) => new()
    {
        Id = tile.Id,
        Kind = tile.Kind,
        Title = tile.Title,
        Text = tile.Text,
        Left = tile.Left,
        Top = tile.Top,
        Width = tile.Width,
        Height = tile.Height,
        IsDraggable = tile.IsDraggable,
        Theme = tile.Theme,
        BorderStyle = tile.BorderStyle,
        CreatedAt = tile.CreatedAt,
        UpdatedAt = tile.UpdatedAt
    };

    private static TileMovementBounds Clone(TileMovementBounds bounds) => new()
    {
        Left = bounds.Left,
        Top = bounds.Top,
        Right = bounds.Right,
        Bottom = bounds.Bottom
    };

    private sealed class TileStore
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public List<Tile>? Tiles { get; set; } = new();
        public TileMovementBounds? MovementBounds { get; set; } = new();
    }
}