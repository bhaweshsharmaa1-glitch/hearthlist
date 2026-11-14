namespace Hearthlist.Backend.Models;

public sealed class TileMovementBounds
{
    public double Left { get; set; } = 60;
    public double Top { get; set; } = 60;
    public double Right { get; set; } = 1220;
    public double Bottom { get; set; } = 680;

    public double Width => Right - Left;
    public double Height => Bottom - Top;
}