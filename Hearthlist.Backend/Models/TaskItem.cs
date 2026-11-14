namespace Hearthlist.Backend.Models;

public sealed class TaskItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int SortOrder { get; set; }
}
