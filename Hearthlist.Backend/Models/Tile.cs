namespace Hearthlist.Backend.Models;

public enum TileKind
{
    Checklist,
    Note
}

public sealed class Tile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public TileKind Kind { get; init; } = TileKind.Note;
    public required string Title { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 160;
    public bool IsDraggable { get; set; } = true;
    public TileThemeVariant Theme { get; set; } = TileThemeVariant.Slate;
    public TileBorderStyle BorderStyle { get; set; } = TileBorderStyle.Accent;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}