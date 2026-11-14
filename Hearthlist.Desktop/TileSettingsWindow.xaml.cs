using System.Globalization;
using System.Windows;
using Hearthlist.Backend.Models;

namespace Hearthlist.Desktop;

public partial class TileSettingsWindow : Window
{
    private readonly Tile? _tile;

    public string? TileTitle { get; private set; }
    public double? TileWidth { get; private set; }
    public double? TileHeight { get; private set; }
    public bool? IsDraggable { get; private set; }
    public TileThemeVariant? TileTheme { get; private set; }
    public TileBorderStyle? BorderStyle { get; private set; }
    public TileMovementBounds? MovementBounds { get; private set; }

    public TileSettingsWindow(Tile? tile, TileMovementBounds bounds)
    {
        InitializeComponent();
        _tile = tile;
        ThemeInput.ItemsSource = Enum.GetValues<TileThemeVariant>();
        BorderStyleInput.ItemsSource = Enum.GetValues<TileBorderStyle>();
        TitleInput.Text = tile?.Title ?? string.Empty;
        WidthInput.Text = (tile?.Width ?? 240).ToString(CultureInfo.InvariantCulture);
        HeightInput.Text = (tile?.Height ?? 160).ToString(CultureInfo.InvariantCulture);
        DragEnabled.IsChecked = tile?.IsDraggable ?? true;
        ThemeInput.SelectedItem = tile?.Theme ?? TileThemeVariant.Slate;
        BorderStyleInput.SelectedItem = tile?.BorderStyle ?? TileBorderStyle.Accent;
        BoundsLeft.Text = bounds.Left.ToString(CultureInfo.InvariantCulture);
        BoundsTop.Text = bounds.Top.ToString(CultureInfo.InvariantCulture);
        BoundsRight.Text = bounds.Right.ToString(CultureInfo.InvariantCulture);
        BoundsBottom.Text = bounds.Bottom.ToString(CultureInfo.InvariantCulture);

        if (tile is null)
        {
            TitleInput.IsEnabled = false;
            WidthInput.IsEnabled = false;
            HeightInput.IsEnabled = false;
            DragEnabled.IsEnabled = false;
            ThemeInput.IsEnabled = false;
            BorderStyleInput.IsEnabled = false;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadNumber(BoundsLeft, out var left) ||
            !TryReadNumber(BoundsTop, out var top) ||
            !TryReadNumber(BoundsRight, out var right) ||
            !TryReadNumber(BoundsBottom, out var bottom) ||
            right - left < 200 || bottom - top < 150)
        {
            MessageBox.Show(this, "Enter a movement area at least 200 by 150 pixels.",
                "Check movement area", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MovementBounds = new TileMovementBounds { Left = left, Top = top, Right = right, Bottom = bottom };
        if (_tile is not null)
        {
            if (!TryReadNumber(WidthInput, out var width) || width < 140 || width > 900 ||
                !TryReadNumber(HeightInput, out var height) || height < 90 || height > 700 ||
                string.IsNullOrWhiteSpace(TitleInput.Text) ||
                ThemeInput.SelectedItem is not TileThemeVariant theme ||
                BorderStyleInput.SelectedItem is not TileBorderStyle borderStyle)
            {
                MessageBox.Show(this, "Enter a title, valid size, and select a theme.",
                    "Check tile settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TileTitle = TitleInput.Text.Trim();
            TileWidth = width;
            TileHeight = height;
            IsDraggable = DragEnabled.IsChecked == true;
            TileTheme = theme;
            BorderStyle = borderStyle;
        }

        DialogResult = true;
    }

    private static bool TryReadNumber(System.Windows.Controls.TextBox textBox, out double value) =>
        double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);
}