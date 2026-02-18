using System.ComponentModel.DataAnnotations;

namespace LiveAuthCore.Entities;

/// <summary>
/// Represents a task in the MissionControl Kanban board
/// </summary>
public class MissionTask
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    
    public TaskType Type { get; set; } = TaskType.Feature;
    
    [MaxLength(100)]
    public string? Owner { get; set; }
    
    public TaskStatus Status { get; set; } = TaskStatus.Backlog;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? DueDate { get; set; }
}

public enum TaskStatus
{
    Backlog = 0,
    Ready = 1,
    InProgress = 2,
    Blocked = 3,
    Review = 4,
    Done = 5
}

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum TaskType
{
    Feature = 0,
    Bug = 1,
    Infra = 2,
    Research = 3
}
