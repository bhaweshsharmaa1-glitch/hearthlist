namespace Hearthlist.Backend.Models;

/// <summary>
/// Maps each creature to its realm's color palette.
/// Hex colors chosen to match the creature descriptions.
/// </summary>
public static class CreaturePalettes
{
    private static readonly Dictionary<Companion, ThemeColors> _palettes = new()
    {
        [Companion.Wisp] = new ThemeColors
        {
            Name = "Wisp — Fox-Spirit Realm",
            Background = "#1a0f00",
            Primary = "#e8781a",
            Secondary = "#c45e10",
            Accent = "#ffaa33",
            Text = "#fff5e6",
            Glow = "#ff8c00",
            ParticleColor = "#ffcc66"
        },
        [Companion.Fable] = new ThemeColors
        {
            Name = "Fable — Owl Realm",
            Background = "#1a1510",
            Primary = "#c89b3c",
            Secondary = "#8b6914",
            Accent = "#e8c55a",
            Text = "#f5edd6",
            Glow = "#d4a828",
            ParticleColor = "#f0d878"
        },
        [Companion.Nook] = new ThemeColors
        {
            Name = "Nook — Hedgehog-Fairy Realm",
            Background = "#0d1a0d",
            Primary = "#6b8f4a",
            Secondary = "#3d6b3d",
            Accent = "#c8e6a0",
            Text = "#e8f5d8",
            Glow = "#88cc66",
            ParticleColor = "#b8e898"
        },
        [Companion.Lumi] = new ThemeColors
        {
            Name = "Lumi — Firefly-Deer Realm",
            Background = "#0a1a0d",
            Primary = "#4a9e5c",
            Secondary = "#2d7a3a",
            Accent = "#88ee88",
            Text = "#d8f5e0",
            Glow = "#66dd77",
            ParticleColor = "#aaffaa"
        },
        [Companion.Sprig] = new ThemeColors
        {
            Name = "Sprig — Leaf-Dragon Realm",
            Background = "#0d1a08",
            Primary = "#3cb043",
            Secondary = "#228b22",
            Accent = "#90ee90",
            Text = "#e0f5d0",
            Glow = "#55cc55",
            ParticleColor = "#88ff88"
        },
        [Companion.Momo] = new ThemeColors
        {
            Name = "Momo — Cloud-Cat Realm",
            Background = "#0f101a",
            Primary = "#7788aa",
            Secondary = "#5566aa",
            Accent = "#ccddff",
            Text = "#e8eeff",
            Glow = "#99aadd",
            ParticleColor = "#bbccff"
        },
        [Companion.Lumo] = new ThemeColors
        {
            Name = "Lumo — Moth-Fairy Realm",
            Background = "#1a1508",
            Primary = "#c88a30",
            Secondary = "#a06820",
            Accent = "#f0c860",
            Text = "#f5ecd0",
            Glow = "#dda840",
            ParticleColor = "#f0d880"
        },
        [Companion.Fen] = new ThemeColors
        {
            Name = "Fen — Moss-Turtle Realm",
            Background = "#081a0d",
            Primary = "#2e8b57",
            Secondary = "#1a6b3a",
            Accent = "#66cc88",
            Text = "#d8f0e0",
            Glow = "#44aa66",
            ParticleColor = "#77dd99"
        },
        [Companion.Yumi] = new ThemeColors
        {
            Name = "Yumi — Rabbit-Kitsune Realm",
            Background = "#10101a",
            Primary = "#aabbcc",
            Secondary = "#8899bb",
            Accent = "#ddeeff",
            Text = "#f0f5ff",
            Glow = "#ccddff",
            ParticleColor = "#e8eeff"
        },
        [Companion.Shadow] = new ThemeColors
        {
            Name = "Shadow — Black Cat Realm",
            Background = "#0d0a14",
            Primary = "#8b6fc0",
            Secondary = "#6a4faf",
            Accent = "#d4a840",
            Text = "#e8e0f0",
            Glow = "#9b7fd0",
            ParticleColor = "#bba8e0"
        }
    };

    public static ThemeColors Get(Companion companion) =>
        _palettes.TryGetValue(companion, out var colors)
            ? colors
            : _palettes[Companion.Shadow];

    public static IReadOnlyDictionary<Companion, ThemeColors> All => _palettes;
}
