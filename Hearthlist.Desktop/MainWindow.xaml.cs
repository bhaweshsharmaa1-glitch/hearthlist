using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Hearthlist.Backend.Models;

namespace Hearthlist.Desktop;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly ObservableCollection<TaskItemViewModel> _tasks = new();

    public MainWindow(App app)
    {
        InitializeComponent();
        _app = app;

        TaskList.ItemsSource = _tasks;

        _app.Backend.Companion.StateChanged += (_, state) =>
            Dispatcher.Invoke(() => UpdateStateBadge(state));

        Loaded += async (_, _) => await LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        _tasks.Clear();
        foreach (var task in _app.Backend.Todos.Tasks)
        {
            _tasks.Add(new TaskItemViewModel(task));
        }
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshFromBackendAsync()
    {
        // Re-sync the view list from the backend source of truth.
        _tasks.Clear();
        foreach (var task in _app.Backend.Todos.Tasks)
        {
            _tasks.Add(new TaskItemViewModel(task));
        }
        UpdateEmptyState();
    }

    private void UpdateStateBadge(CompanionState state)
    {
        var text = state switch
        {
            CompanionState.Idle => "Idle",
            CompanionState.Happy => "Happy",
            CompanionState.Sleepy => "Sleepy",
            CompanionState.Greeting => "Welcome",
            _ => "Idle"
        };
        StateText.Text = text;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        await AddTaskAsync();
    }

    private async void TitleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await AddTaskAsync();
    }

    private async Task AddTaskAsync()
    {
        var title = TitleInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return;

        await _app.Backend.Todos.AddAsync(title);
        TitleInput.Clear();
        await RefreshFromBackendAsync();

        // Register interaction to keep the companion awake.
        _app.Backend.Companion.RegisterActivity();
    }

    private async void CheckBox_Checked(object sender, RoutedEventArgs e)
    {
        await HandleToggleAsync(sender, true);
    }

    private async void CheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        await HandleToggleAsync(sender, false);
    }

    private async Task HandleToggleAsync(object sender, bool completed)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskItemViewModel vm)
            return;

        await _app.Backend.Todos.SetCompletedAsync(vm.Id, completed);
        if (completed)
        {
            _app.Backend.Companion.TriggerHappy();
        }
        else
        {
            _app.Backend.Companion.RegisterActivity();
        }

        vm.Update();
    }

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskItemViewModel vm)
            return;
        await MoveAsync(vm, -1);
    }

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskItemViewModel vm)
            return;
        await MoveAsync(vm, 1);
    }

    private async Task MoveAsync(TaskItemViewModel vm, int delta)
    {
        int from = _tasks.IndexOf(vm);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _tasks.Count)
            return;

        await _app.Backend.Todos.ReorderAsync(from, to);
        await RefreshFromBackendAsync();
        _app.Backend.Companion.RegisterActivity();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskItemViewModel vm)
            return;

        var dialog = new EditTaskDialog(vm.Title, vm.Description);
        if (dialog.ShowDialog() == true)
        {
            await _app.Backend.Todos.UpdateAsync(vm.Id, dialog.TaskTitle, dialog.TaskDescription);
            await RefreshFromBackendAsync();
            _app.Backend.Companion.RegisterActivity();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskItemViewModel vm)
            return;

        await _app.Backend.Todos.DeleteAsync(vm.Id);
        await RefreshFromBackendAsync();
        _app.Backend.Companion.RegisterActivity();
    }
}

public sealed class TaskItemViewModel : INotifyPropertyChanged
{
    private readonly TaskItem _item;

    public TaskItemViewModel(TaskItem item)
    {
        _item = item;
    }

    public string Id => _item.Id;
    public string Title => _item.Title;
    public string Description => _item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool Completed
    {
        get => _item.Completed;
        set
        {
            if (_item.Completed == value)
                return;
            _item.Completed = value;
            OnPropertyChanged(nameof(Completed));
            OnPropertyChanged(nameof(Title)); // refresh strike-through via style trigger
        }
    }

    public void Update()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(Completed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
