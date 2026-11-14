using System.Text.Json;
using Hearthlist.Backend.Models;

namespace Hearthlist.Backend.Services;

/// <summary>
/// CRUD for to-do tasks, persisted via <see cref="StorageProvider"/>.
/// Uses LocalFolder when packaged (MSIX), or a plain AppData folder when unpackaged.
///
/// Thread-safety: all access to the in-memory <see cref="_tasks"/> list is
/// serialized with <see cref="_listLock"/>; file I/O is additionally serialized
/// with <see cref="_fileLock"/>. The method bodies are not atomic across the
/// in-memory mutation and the awaited save, so to keep ordering safe for
/// single-threaded UI hosts while remaining race-free under concurrency, each
/// public method performs its list mutation under _listLock and swaps in a
/// fresh snapshot before the awaited file write.
/// </summary>
public class TodoService
{
    private const string TasksFileName = "tasks.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private List<TaskItem> _tasks = new();
    private readonly object _listLock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public IReadOnlyList<TaskItem> Tasks
    {
        get
        {
            lock (_listLock)
            {
                return _tasks.OrderBy(t => t.SortOrder).ToList();
            }
        }
    }

    /// <summary>Raised whenever the task collection changes (add/edit/delete/reorder/toggle).</summary>
    public event EventHandler<IReadOnlyList<TaskItem>>? TasksChanged;

    public async Task InitializeAsync()
    {
        List<TaskItem> loaded;
        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetFileIfExistsAsync(TasksFileName);
            var raw = handle is null ? string.Empty : await StorageProvider.ReadTextAsync(handle);

            loaded = string.IsNullOrWhiteSpace(raw)
                ? new List<TaskItem>()
                : JsonSerializer.Deserialize<List<TaskItem>>(raw, _jsonOptions)
                  ?? new List<TaskItem>();
        }
        finally
        {
            _fileLock.Release();
        }

        lock (_listLock)
        {
            _tasks = loaded;
        }
    }

    public async Task<TaskItem> AddAsync(string title, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        var task = new TaskItem
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty
        };

        lock (_listLock)
        {
            task.SortOrder = _tasks.Count > 0 ? _tasks.Max(t => t.SortOrder) + 1 : 0;
            _tasks.Add(task);
        }

        await SaveAndNotifyAsync();
        return task;
    }

    public async Task<bool> UpdateAsync(string id, string? title = null, string? description = null)
    {
        lock (_listLock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null)
                return false;

            if (!string.IsNullOrWhiteSpace(title))
                task.Title = title.Trim();

            if (description is not null)
                task.Description = description.Trim();
        }

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task<bool> SetCompletedAsync(string id, bool completed)
    {
        lock (_listLock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null)
                return false;

            task.Completed = completed;
            task.CompletedAt = completed ? DateTime.UtcNow : null;
        }

        await SaveAndNotifyAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        lock (_listLock)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task is null)
                return false;

            _tasks.Remove(task);
            ReindexInPlace();
        }

        await SaveAndNotifyAsync();
        return true;
    }

    /// <summary>
    /// Reorders tasks by moving an item's SortOrder relative to the others.
    /// <paramref name="fromIndex"/>/<paramref name="toIndex"/> are 0-based positions in the current list.
    /// </summary>
    public async Task ReorderAsync(int fromIndex, int toIndex)
    {
        lock (_listLock)
        {
            var list = _tasks.OrderBy(t => t.SortOrder).ToList();

            if (fromIndex < 0 || toIndex < 0 || fromIndex >= list.Count || toIndex >= list.Count)
                return;

            var moving = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, moving);

            for (int i = 0; i < list.Count; i++)
                list[i].SortOrder = i;
        }

        await SaveAndNotifyAsync();
    }

    private void ReindexInPlace()
    {
        var ordered = _tasks.OrderBy(t => t.SortOrder).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].SortOrder = i;
    }

    private async Task SaveAndNotifyAsync()
    {
        string json;
        lock (_listLock)
        {
            json = JsonSerializer.Serialize(_tasks, _jsonOptions);
        }

        await _fileLock.WaitAsync();
        try
        {
            var handle = await StorageProvider.GetOrCreateFileAsync(TasksFileName);
            await StorageProvider.WriteTextAsync(handle, json);
        }
        finally
        {
            _fileLock.Release();
        }

        IReadOnlyList<TaskItem> snapshot = Tasks; // safe snapshot under lock
        TasksChanged?.Invoke(this, snapshot);
    }
}
