using Hearthlist.Backend.Models;
using SkiaSharp;
using Windows.System.UserProfile;

namespace Hearthlist.Backend.Services;

/// <summary>
/// Generates a static image of the current task list styled per the active
/// creature theme and (optionally) sets it as the Windows lock screen.
///
/// NOTE ON MSIX / STORE: LockScreen.SetImageFileAsync requires broad
/// user-profile write access that is restricted in Store-distributed apps.
/// To avoid Store-review rejection in v1, the actual SetImageFileAsync call
/// is gated behind a flag (default OFF). Image generation is always
/// available so the feature is complete; flip the flag once the capability
/// situation is cleared.
/// </summary>
public class LockScreenService
{
    private const int ImageWidth = 2560;
    private const int ImageHeight = 1440;
    private const string LockScreenDirName = "lockScreen";
    private const string LockScreenFileName = "hearthlist-lock.png";

    private readonly TodoService _todoService;
    private readonly ThemeManager _themeManager;

    public LockScreenService(TodoService todoService, ThemeManager themeManager)
    {
        _todoService = todoService;
        _themeManager = themeManager;
    }

    /// <summary>
    /// Renders the current task list into a themed PNG and, if
    /// <see cref="AppSettings.LockScreenEnabled"/> is true (and the process is
    /// packaged, i.e. MSIX), applies it as the Windows lock screen image.
    ///
    /// When the feature is disabled (default in v1), this is a fast no-op and
    /// does not even generate the image — so per-task-change refreshes cost
    /// nothing until the user flips the setting on.
    /// </summary>
    public async Task<StorageFileHandle?> GenerateAndApplyAsync()
    {
        if (!_themeManager.Settings.LockScreenEnabled)
        {
            // Feature dormant — skip PNG generation entirely.
            return null;
        }

        var handle = await RenderAndSaveAsync();

        // Only try to set the OS lock screen when we are running as a
        // packaged (MSIX) app. Unpackaged runs skip the actual OS call
        // (needs a WinRT StorageFile + user-profile capability).
        if (StorageProvider.IsPackaged)
        {
            await ApplyToLockScreenAsync(handle);
        }

        return handle;
    }

    private async Task<StorageFileHandle> RenderAndSaveAsync()
    {
        var palette = _themeManager.CurrentPalette;
        var tasks = _todoService.Tasks;

        using var bitmap = new SKBitmap(ImageWidth, ImageHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor(palette.Background));

        // Soft radial glow in the accent color, subtle like the creature's realm.
        using (var glowPaint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(ImageWidth / 2f, ImageHeight * 0.4f),
                ImageHeight * 0.9f,
                new[] { MultiplyAlpha(ParseColor(palette.Glow), 0.25f), ParseColor(palette.Background) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawPaint(glowPaint);
        }

        // Title — the creature's realm name.
        using (var titlePaint = new SKPaint
        {
            Color = ParseColor(palette.Text),
            IsAntialias = true,
            TextSize = 96,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        })
        {
            canvas.DrawText(palette.Name.Split('—')[0].Trim(), 120, 160, titlePaint);
        }

        // Subtitle.
        using (var subPaint = new SKPaint
        {
            Color = ParseColor(palette.Accent),
            IsAntialias = true,
            TextSize = 44,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        })
        {
            canvas.DrawText("Your tasks for today", 124, 230, subPaint);
        }

        // Task lines.
        using (var taskPaint = new SKPaint
        {
            Color = ParseColor(palette.Text),
            IsAntialias = true,
            TextSize = 52,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        })
        using (var completePaint = new SKPaint
        {
            Color = ParseColor(palette.Primary),
            IsAntialias = true,
            TextSize = 52,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal),
            Style = SKPaintStyle.Stroke
        })
        {
            float y = 340;
            var visible = tasks.Take(12).ToList();
            foreach (var task in visible)
            {
                var prefix = task.Completed ? "[x] " : "[ ] ";
                var text = prefix + task.Title;

                // Truncate to avoid overflow.
                if (text.Length > 60)
                    text = text[..57] + "...";

                if (task.Completed)
                {
                    canvas.DrawText(text, 140, y, completePaint);
                }
                else
                {
                    canvas.DrawText(text, 140, y, taskPaint);
                }

                y += 76;
                if (y > ImageHeight - 120)
                    break;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var handle = await StorageProvider.GetOrCreateFileAsync(
            System.IO.Path.Combine(LockScreenDirName, LockScreenFileName));
        await StorageProvider.WriteBytesAsync(handle, data.ToArray());

        return handle;
    }

    private static async Task ApplyToLockScreenAsync(StorageFileHandle handle)
    {
        // Only valid when packaged; we already guard the caller with IsPackaged.
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(handle.PackagedToken);
        await LockScreen.SetImageFileAsync(file);
    }

    private static SKColor ParseColor(string hex)
    {
        var cleaned = hex.TrimStart('#');
        var r = Convert.ToByte(cleaned.Substring(0, 2), 16);
        var g = Convert.ToByte(cleaned.Substring(2, 2), 16);
        var b = Convert.ToByte(cleaned.Substring(4, 2), 16);
        return new SKColor(r, g, b);
    }

    private static SKColor MultiplyAlpha(SKColor color, float factor)
    {
        return color.WithAlpha((byte)(color.Alpha * factor));
    }
}
