namespace Hearthlist.Backend.Models;

/// <summary>
/// Represents a color palette for a creature's realm.
/// Hex values (e.g. "#1a1a2e") — renderer interprets these.
/// </summary>
public sealed class ThemeColors
{
    public required string Name { get; init; }
    public required string Background { get; init; }
    public required string Primary { get; init; }
    public required string Secondary { get; init; }
    public required string Accent { get; init; }
    public required string Text { get; init; }
    public required string Glow { get; init; }
    public required string ParticleColor { get; init; }
}
