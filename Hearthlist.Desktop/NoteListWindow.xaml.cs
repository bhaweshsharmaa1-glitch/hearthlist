using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Hearthlist.Backend.Models;

namespace Hearthlist.Desktop;

public partial class NoteListWindow : Window
{
    private const double MaxSpeed = 190;
    private const double DampingPerFrame = 0.997;

    private readonly App _app;
    private readonly Dictionary<string, FloatingTileWindow> _tileWindows = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tileLocks = new();
    private readonly ConcurrentDictionary<string, byte> _deletedTileIds = new();
    private readonly DispatcherTimer _physicsTimer;
    private readonly DispatcherTimer _positionSaveTimer;
    private readonly Stopwatch _physicsClock = Stopwatch.StartNew();
    private bool _savingPositions;
    private bool _closeInProgress;
    private bool _allowClose;
    private double _lastPhysicsSeconds;
    private bool _updatingThemeSelector;

    public NoteListWindow(App app)
    {
        InitializeComponent();
        _app = app;

        ThemeSelector.ItemsSource = Enum.GetValues<TileThemeVariant>();
        ThemeSelector.SelectedItem = _app.Backend.Themes.CurrentTileTheme;
        ThemeSelector.SelectionChanged += ThemeSelector_SelectionChanged;
        _app.Backend.Tiles.TilesChanged += Tiles_TilesChanged;

        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _physicsTimer.Tick += PhysicsTimer_Tick;
        _physicsTimer.Start();

        _positionSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _positionSaveTimer.Tick += PositionSaveTimer_Tick;
        _positionSaveTimer.Start();

        RefreshTileWindows();
    }

    private void Tiles_TilesChanged(object? sender, IReadOnlyList<Tile> tiles)
    {
        Dispatcher.BeginInvoke(RefreshTileWindows);
    }

    private void RefreshTileWindows()
    {
        var tiles = _app.Backend.Tiles.Tiles
            .Where(tile => tile.Kind == TileKind.Note)
            .ToDictionary(tile => tile.Id);

        foreach (var staleId in _tileWindows.Keys.Where(id => !tiles.ContainsKey(id)).ToList())
        {
            _tileWindows[staleId].Close();
            _tileWindows.Remove(staleId);
        }

        foreach (var tile in tiles.Values)
        {
            if (_tileWindows.TryGetValue(tile.Id, out var existing))
            {
                existing.UpdateFromTile(tile);
                continue;
            }

            var window = new FloatingTileWindow(tile);
            window.ActionRequested += TileWindow_ActionRequested;
            _tileWindows[tile.Id] = window;
            window.Show();
        }
    }

    private async void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingThemeSelector || ThemeSelector.SelectedItem is not TileThemeVariant theme ||
            theme == _app.Backend.Themes.CurrentTileTheme)
        {
            return;
        }

        try
        {
            await _app.SetTileThemeAsync(theme);
        }
        catch (Exception exception)
        {
            _updatingThemeSelector = true;
            ThemeSelector.SelectedItem = _app.Backend.Themes.CurrentTileTheme;
            _updatingThemeSelector = false;
            ShowError("The default theme could not be saved.", exception);
        }
    }

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var bounds = _app.Backend.Tiles.MovementBounds;
        var width = 250d;
        var height = 170d;
        var left = Math.Clamp(bounds.Left + 36 + (_tileWindows.Count % 5) * 28, bounds.Left, bounds.Right - width);
        var top = Math.Clamp(bounds.Top + 36 + (_tileWindows.Count % 5) * 24, bounds.Top, bounds.Bottom - height);

        try
        {
            await _app.Backend.Tiles.AddAsync(
                "New note",
                kind: TileKind.Note,
                left: left,
                top: top,
                theme: _app.Backend.Themes.CurrentTileTheme);
        }
        catch (Exception exception)
        {
            ShowError("The tile could not be created.", exception);
        }
    }

    private void MovementAreaButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TileSettingsWindow(null, _app.Backend.Tiles.MovementBounds) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.MovementBounds is null)
            return;

        _ = SaveMovementBoundsAsync(dialog.MovementBounds);
    }

    private async Task SaveMovementBoundsAsync(TileMovementBounds bounds)
    {
        try
        {
            await _app.Backend.Tiles.UpdateMovementBoundsAsync(bounds);
            foreach (var tileWindow in _tileWindows.Values)
                KeepTileInBounds(tileWindow, bounds);
            await SaveAllPositionsAsync();
        }
        catch (Exception exception)
        {
            ShowError("The movement area could not be saved.", exception);
        }
    }

    private async void TileWindow_ActionRequested(FloatingTileWindow window, string action)
    {
        switch (action)
        {
            case "edit":
                OpenEditor(window.Tile, window);
                break;
            case "settings":
                await ShowTileSettingsAsync(window);
                break;
            case "delete":
                await DeleteTileAsync(window);
                break;
            case "position":
                break;
        }
    }

    private void OpenEditor(Tile tile, Window owner)
    {
        var editor = new NoteEditorWindow(tile.Text) { Owner = owner };
        editor.TextCommitted += text => UpdateNoteTextAsync(tile.Id, text);
        editor.Show();
        editor.Activate();
    }

    private async Task UpdateNoteTextAsync(string id, string text)
    {
        var tileLock = _tileLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await tileLock.WaitAsync();
        try
        {
            if (_deletedTileIds.ContainsKey(id))
                return;

            var updated = await Task.Run(() => _app.Backend.Tiles.UpdateTextAsync(id, text));
            if (!updated)
                throw new InvalidOperationException("This tile no longer exists.");
        }
        finally
        {
            tileLock.Release();
        }
    }

    private async Task ShowTileSettingsAsync(FloatingTileWindow window)
    {
        var dialog = new TileSettingsWindow(window.Tile, _app.Backend.Tiles.MovementBounds)
        {
            Owner = window
        };
        if (dialog.ShowDialog() != true || dialog.MovementBounds is null)
            return;

        try
        {
            await _app.Backend.Tiles.UpdateSettingsAsync(
                window.Tile.Id,
                dialog.TileTitle,
                dialog.TileWidth,
                dialog.TileHeight,
                dialog.IsDraggable,
                dialog.TileTheme,
                dialog.BorderStyle);
            await _app.Backend.Tiles.UpdateMovementBoundsAsync(dialog.MovementBounds);
            foreach (var tileWindow in _tileWindows.Values)
                KeepTileInBounds(tileWindow, dialog.MovementBounds);
            await SaveAllPositionsAsync();
        }
        catch (Exception exception)
        {
            ShowError("Tile settings could not be saved.", exception);
        }
    }

    private async Task DeleteTileAsync(FloatingTileWindow window)
    {
        if (MessageBox.Show(window, $"Remove '{window.Tile.Title}'?", "Remove tile",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var id = window.Tile.Id;
        var tileLock = _tileLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        _deletedTileIds.TryAdd(id, 0);
        await tileLock.WaitAsync();
        try
        {
            await Task.Run(() => _app.Backend.Tiles.DeleteAsync(id));
        }
        catch (Exception exception)
        {
            _deletedTileIds.TryRemove(id, out _);
            ShowError("The tile could not be removed.", exception);
        }
        finally
        {
            tileLock.Release();
        }
    }

    private void PhysicsTimer_Tick(object? sender, EventArgs e)
    {
        var now = _physicsClock.Elapsed.TotalSeconds;
        var delta = Math.Clamp(now - _lastPhysicsSeconds, 0, 0.05);
        _lastPhysicsSeconds = now;
        if (delta == 0)
            return;

        var bounds = _app.Backend.Tiles.MovementBounds;
        var windows = _tileWindows.Values.ToList();
        var damping = Math.Pow(DampingPerFrame, delta * 60);

        foreach (var window in windows)
        {
            if (window.IsBeingDragged)
                continue;

            window.VelocityX = Math.Clamp(window.VelocityX * damping, -MaxSpeed, MaxSpeed);
            window.VelocityY = Math.Clamp(window.VelocityY * damping, -MaxSpeed, MaxSpeed);
            window.Left += window.VelocityX * delta;
            window.Top += window.VelocityY * delta;
            CollideWithBoundary(window, bounds);
        }

        for (var first = 0; first < windows.Count; first++)
        {
            for (var second = first + 1; second < windows.Count; second++)
                ResolveTileCollision(windows[first], windows[second]);
        }
    }

    private static void CollideWithBoundary(FloatingTileWindow window, TileMovementBounds bounds)
    {
        var minLeft = bounds.Left;
        var maxLeft = Math.Max(minLeft, bounds.Right - window.Width);
        var minTop = bounds.Top;
        var maxTop = Math.Max(minTop, bounds.Bottom - window.Height);

        if (window.Left < minLeft)
        {
            window.Left = minLeft;
            window.VelocityX = Math.Abs(window.VelocityX) * 0.92;
        }
        else if (window.Left > maxLeft)
        {
            window.Left = maxLeft;
            window.VelocityX = -Math.Abs(window.VelocityX) * 0.92;
        }

        if (window.Top < minTop)
        {
            window.Top = minTop;
            window.VelocityY = Math.Abs(window.VelocityY) * 0.92;
        }
        else if (window.Top > maxTop)
        {
            window.Top = maxTop;
            window.VelocityY = -Math.Abs(window.VelocityY) * 0.92;
        }
    }

    private static void KeepTileInBounds(FloatingTileWindow window, TileMovementBounds bounds)
    {
        window.Left = Math.Clamp(window.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - window.Width));
        window.Top = Math.Clamp(window.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - window.Height));
    }

    private static void ResolveTileCollision(FloatingTileWindow first, FloatingTileWindow second)
    {
        const double cornerRadius = 16;
        var firstCenterX = first.Left + first.Width / 2;
        var firstCenterY = first.Top + first.Height / 2;
        var secondCenterX = second.Left + second.Width / 2;
        var secondCenterY = second.Top + second.Height / 2;
        var dx = secondCenterX - firstCenterX;
        var dy = secondCenterY - firstCenterY;
        var halfWidth = first.Width / 2 + second.Width / 2 - cornerRadius;
        var halfHeight = first.Height / 2 + second.Height / 2 - cornerRadius;
        var overlapX = halfWidth - Math.Abs(dx);
        var overlapY = halfHeight - Math.Abs(dy);

        double normalX;
        double normalY;
        double overlap;
        if (overlapX > 0 && overlapY > 0)
        {
            if (overlapX < overlapY)
            {
                normalX = dx >= 0 ? 1 : -1;
                normalY = 0;
                overlap = overlapX;
            }
            else
            {
                normalX = 0;
                normalY = dy >= 0 ? 1 : -1;
                overlap = overlapY;
            }
        }
        else
        {
            var cornerX = Math.Max(Math.Abs(dx) - (first.Width / 2 + second.Width / 2 - 2 * cornerRadius), 0);
            var cornerY = Math.Max(Math.Abs(dy) - (first.Height / 2 + second.Height / 2 - 2 * cornerRadius), 0);
            var distance = Math.Sqrt(cornerX * cornerX + cornerY * cornerY);
            if (distance >= cornerRadius * 2 || Math.Abs(dx) <= halfWidth - cornerRadius || Math.Abs(dy) <= halfHeight - cornerRadius)
                return;

            var centerDistance = Math.Sqrt(dx * dx + dy * dy);
            if (centerDistance < 0.001)
                return;

            normalX = dx / centerDistance;
            normalY = dy / centerDistance;
            overlap = cornerRadius * 2 - distance;
        }

        var firstDragging = first.IsBeingDragged;
        var secondDragging = second.IsBeingDragged;
        if (firstDragging && secondDragging)
            return;

        if (!firstDragging && !secondDragging)
        {
            first.Left -= normalX * overlap / 2;
            first.Top -= normalY * overlap / 2;
            second.Left += normalX * overlap / 2;
            second.Top += normalY * overlap / 2;
        }
        else if (firstDragging)
        {
            second.Left += normalX * overlap;
            second.Top += normalY * overlap;
        }
        else
        {
            first.Left -= normalX * overlap;
            first.Top -= normalY * overlap;
        }

        var relativeX = second.VelocityX - first.VelocityX;
        var relativeY = second.VelocityY - first.VelocityY;
        var velocityAlongNormal = relativeX * normalX + relativeY * normalY;
        if (velocityAlongNormal > 0)
            return;

        const double restitution = 0.92;
        var impulse = -(1 + restitution) * velocityAlongNormal / (1 / GetMass(first) + 1 / GetMass(second));
        var impulseX = impulse * normalX;
        var impulseY = impulse * normalY;
        if (!firstDragging)
        {
            first.VelocityX = Math.Clamp(first.VelocityX - impulseX / GetMass(first), -MaxSpeed, MaxSpeed);
            first.VelocityY = Math.Clamp(first.VelocityY - impulseY / GetMass(first), -MaxSpeed, MaxSpeed);
        }
        if (!secondDragging)
        {
            second.VelocityX = Math.Clamp(second.VelocityX + impulseX / GetMass(second), -MaxSpeed, MaxSpeed);
            second.VelocityY = Math.Clamp(second.VelocityY + impulseY / GetMass(second), -MaxSpeed, MaxSpeed);
        }
    }

    private static double GetMass(FloatingTileWindow window) => window.Width * window.Height;

    private async void PositionSaveTimer_Tick(object? sender, EventArgs e) => await SaveAllPositionsAsync();

    private async Task SaveAllPositionsAsync()
    {
        if (_savingPositions || _tileWindows.Count == 0)
            return;

        _savingPositions = true;
        try
        {
            var positions = _tileWindows.Values.ToDictionary(
                window => window.Tile.Id,
                window => (window.Left, window.Top));
            await Task.Run(() => _app.Backend.Tiles.UpdatePositionsAsync(positions));
        }
        catch (Exception exception)
        {
            ShowError("Tile positions could not be saved.", exception);
        }
        finally
        {
            _savingPositions = false;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        if (_closeInProgress)
            return;

        _closeInProgress = true;
        _physicsTimer.Stop();
        _positionSaveTimer.Stop();
        _app.Backend.Tiles.TilesChanged -= Tiles_TilesChanged;

        try
        {
            while (_savingPositions)
                await Task.Delay(25);

            var positions = _tileWindows.Values.ToDictionary(
                window => window.Tile.Id,
                window => (window.Left, window.Top));
            await Task.Run(() => _app.Backend.Tiles.UpdatePositionsAsync(positions));

            foreach (var window in _tileWindows.Values.ToList())
                window.Close();
            _tileWindows.Clear();
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            _app.Backend.Tiles.TilesChanged += Tiles_TilesChanged;
            _physicsTimer.Start();
            _positionSaveTimer.Start();
            _closeInProgress = false;
            ShowError("Tile positions could not be saved. The app will stay open.", exception);
        }
    }

    private void ShowError(string message, Exception exception)
    {
        MessageBox.Show(this, $"{message}\n\n{exception.Message}",
            "Hearthlist", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}