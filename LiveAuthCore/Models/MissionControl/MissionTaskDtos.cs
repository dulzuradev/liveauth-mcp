using System.ComponentModel.DataAnnotations;
using LiveAuthCore.Entities;

namespace LiveAuthCore.Models.MissionControl;

/// <summary>
/// DTO for creating a new task
/// </summary>
public class CreateTaskDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    
    public TaskType Type { get; set; } = TaskType.Feature;
    
    [MaxLength(100)]
    public string? Owner { get; set; }
    
    public DateTime? DueDate { get; set; }
}

/// <summary>
/// DTO for updating an existing task
/// </summary>
public class UpdateTaskDto
{
    [MaxLength(200)]
    public string? Title { get; set; }
    
    [MaxLength(2000)]
    public string? Description { get; set; }
    
    public TaskPriority? Priority { get; set; }
    
    public TaskType? Type { get; set; }
    
    [MaxLength(100)]
    public string? Owner { get; set; }
    
    public Entities.TaskStatus? Status { get; set; }
    
    public DateTime? DueDate { get; set; }
}

/// <summary>
/// DTO for task response
/// </summary>
public class TaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; }
    public TaskType Type { get; set; }
    public string? Owner { get; set; }
    public Entities.TaskStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    
    public static TaskDto FromEntity(MissionTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Priority = task.Priority,
        Type = task.Type,
        Owner = task.Owner,
        Status = task.Status,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt,
        DueDate = task.DueDate
    };
}

/// <summary>
/// DTO for filtering tasks
/// </summary>
public class TaskFilterDto
{
    public TaskType? Type { get; set; }
    public TaskPriority? Priority { get; set; }
    public Entities.TaskStatus? Status { get; set; }
}

/// <summary>
/// DTO for moving a task to a different column
/// </summary>
public class MoveTaskDto
{
    [Required]
    public Entities.TaskStatus Status { get; set; }
}
