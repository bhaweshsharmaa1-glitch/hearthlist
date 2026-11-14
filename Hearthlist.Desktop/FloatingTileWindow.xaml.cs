using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hearthlist.Backend.Models;

namespace Hearthlist.Desktop;

public partial class FloatingTileWindow : Window
{
    private readonly List<(Point Position, DateTime Time)> _dragSamples = new();
    private Point _dragOffset;
    private bool _isDragging;

    public Tile Tile { get; private set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public bool IsBeingDragged => _isDragging;

    public event Action<FloatingTileWindow, string>? ActionRequested;

    public FloatingTileWindow(Tile tile)
    {
        InitializeComponent();
        Tile = tile;
        VelocityX = (Random.Shared.NextDouble() - 0.5) * 60;
        VelocityY = (Random.Shared.NextDouble() - 0.5) * 60;
        UpdateFromTile(tile, applyPosition: true);
    }

    public void UpdateFromTile(Tile tile, bool applyPosition = false)
    {
        Tile = tile;
        Title = tile.Title;
        TileTitle.Text = tile.Title;
        TileText.Text = tile.Text;
        Width = Math.Clamp(tile.Width, MinWidth, 900);
        Height = Math.Clamp(tile.Height, MinHeight, 700);
        if (applyPosition)
        {
            Left = tile.Left;
            Top = tile.Top;
        }
        Cursor = tile.IsDraggable ? Cursors.SizeAll : Cursors.Arrow;
        ApplyTheme(tile.Theme);
        AccentRim.Visibility = tile.BorderStyle is TileBorderStyle.Accent or TileBorderStyle.Glow
            ? Visibility.Visible
            : Visibility.Collapsed;
        AccentRim.Opacity = tile.BorderStyle == TileBorderStyle.Glow ? 0.9 : 0.52;
        GlowRim.Visibility = tile.BorderStyle == TileBorderStyle.Glow
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    private void ApplyTheme(TileThemeVariant theme)
    {
        if (!Enum.IsDefined(theme))
            theme = TileThemeVariant.Slate;

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

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ActionRequested?.Invoke(this, "edit");
            e.Handled = true;
            return;
        }

        if (!Tile.IsDraggable)
            return;

        _dragOffset = e.GetPosition(this);
        _dragSamples.Clear();
        _dragSamples.Add((new Point(Left, Top), DateTime.UtcNow));
        _isDragging = true;
        VelocityX = 0;
        VelocityY = 0;
        CaptureMouse();
        e.Handled = true;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var screenPoint = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screenPoint.X / dpi.DpiScaleX - _dragOffset.X;
        Top = screenPoint.Y / dpi.DpiScaleY - _dragOffset.Y;
        _dragSamples.Add((new Point(Left, Top), DateTime.UtcNow));
        if (_dragSamples.Count > 6)
            _dragSamples.RemoveAt(0);
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        ReleaseMouseCapture();
        if (_dragSamples.Count >= 2)
        {
            var first = _dragSamples[0];
            var last = _dragSamples[^1];
            var seconds = Math.Max((last.Time - first.Time).TotalSeconds, 0.001);
            VelocityX = Math.Clamp((last.Position.X - first.Position.X) / seconds, -190, 190);
            VelocityY = Math.Clamp((last.Position.Y - first.Position.Y) / seconds, -190, 190);
        }

        ActionRequested?.Invoke(this, "position");
        e.Handled = true;
    }

    private void EditNote_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, "edit");
    private void TileSettings_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, "settings");
    private void RemoveTile_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, "delete");
}