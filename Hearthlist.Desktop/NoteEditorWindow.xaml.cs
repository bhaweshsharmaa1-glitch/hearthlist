using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Hearthlist.Desktop;

public partial class NoteEditorWindow : Window
{
    private const int MaximumNoteLength = 500_000;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly SemaphoreSlim _commitLock = new(1, 1);
    private string _lastCommittedText;
    private bool _isInitializing = true;
    private bool _closeInProgress;
    private bool _allowClose;

    public event Func<string, Task>? TextCommitted;

    public NoteEditorWindow(string initialText, Rect? anchor = null)
    {
        InitializeComponent();

        var text = initialText ?? string.Empty;
        if (text.Length > MaximumNoteLength)
            text = text[..MaximumNoteLength];

        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        EditorTextBox.Text = text;
        _lastCommittedText = text;
        _isInitializing = false;
        SaveStatus.Text = "Saved on this device";
        Loaded += (_, _) => EditorTextBox.Focus();
        Closing += Window_Closing;
        PreviewKeyDown += Window_PreviewKeyDown;

        if (anchor is { IsEmpty: false } bounds && bounds.Width > 0 && bounds.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = Math.Max(MinWidth, bounds.Width);
            Height = Math.Max(MinHeight, bounds.Height);
        }
    }

    private void EditorTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        SaveStatus.Text = "Unsaved changes";
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        try
        {
            await CommitIfChangedAsync();
        }
        catch (Exception exception)
        {
            SaveStatus.Text = "Save failed";
            MessageBox.Show(this, $"The note could not be saved.\n\n{exception.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _autosaveTimer.Stop();
        try
        {
            await CommitIfChangedAsync();
            _allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            _closeInProgress = false;
            SaveStatus.Text = "Save failed";
            MessageBox.Show(this, $"The note could not be saved, so it remains open.\n\n{exception.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async Task CommitIfChangedAsync()
    {
        await _commitLock.WaitAsync();
        try
        {
            var text = EditorTextBox.Text;
            if (text == _lastCommittedText)
                return;

            var handlers = TextCommitted;
            if (handlers is null)
            {
                SaveStatus.Text = "Not saved";
                return;
            }

            foreach (Func<string, Task> handler in handlers.GetInvocationList())
                await handler(text);

            _lastCommittedText = text;
            SaveStatus.Text = "Saved on this device";
        }
        finally
        {
            _commitLock.Release();
        }
    }
}