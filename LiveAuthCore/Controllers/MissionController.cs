using LiveAuthCore.Entities;
using LiveAuthCore.Models.MissionControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskStatus = LiveAuthCore.Entities.TaskStatus;

namespace LiveAuthCore.Controllers;

/// <summary>
/// MissionControl API - Kanban board for tracking LiveAuth tasks
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MissionController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public MissionController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get all tasks with optional filtering
    /// </summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks([FromQuery] TaskFilterDto? filter)
    {
        var query = _dbContext.MissionTasks.AsQueryable();

        if (filter?.Type.HasValue == true)
            query = query.Where(t => t.Type == filter.Type.Value);

        if (filter?.Priority.HasValue == true)
            query = query.Where(t => t.Priority == filter.Priority.Value);

        if (filter?.Status.HasValue == true)
            query = query.Where(t => t.Status == filter.Status.Value);

        var tasks = await query
            .OrderBy(t => t.Status)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => TaskDto.FromEntity(t))
            .ToListAsync();

        return Ok(tasks);
    }

    /// <summary>
    /// Get a single task by ID
    /// </summary>
    [HttpGet("tasks/{id:int}")]
    public async Task<IActionResult> GetTask(int id)
    {
        var task = await _dbContext.MissionTasks.FindAsync(id);
        if (task == null)
            return NotFound(new { error = "Task not found" });

        return Ok(TaskDto.FromEntity(task));
    }

    /// <summary>
    /// Create a new task
    /// </summary>
    [HttpPost("tasks")]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Title is required" });

        var task = new MissionTask
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Priority = dto.Priority,
            Type = dto.Type,
            Owner = dto.Owner?.Trim(),
            Status = TaskStatus.Backlog,
            DueDate = dto.DueDate,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MissionTasks.Add(task);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTask), new { id = task.Id }, TaskDto.FromEntity(task));
    }

    /// <summary>
    /// Update an existing task
    /// </summary>
    [HttpPut("tasks/{id:int}")]
    public async Task<IActionResult> UpdateTask(int id, [FromBody] UpdateTaskDto dto)
    {
        var task = await _dbContext.MissionTasks.FindAsync(id);
        if (task == null)
            return NotFound(new { error = "Task not found" });

        if (!string.IsNullOrWhiteSpace(dto.Title))
            task.Title = dto.Title.Trim();

        if (dto.Description != null)
            task.Description = dto.Description.Trim();

        if (dto.Priority.HasValue)
            task.Priority = dto.Priority.Value;

        if (dto.Type.HasValue)
            task.Type = dto.Type.Value;

        if (dto.Owner != null)
            task.Owner = string.IsNullOrWhiteSpace(dto.Owner) ? null : dto.Owner.Trim();

        if (dto.Status.HasValue)
            task.Status = dto.Status.Value;

        if (dto.DueDate.HasValue)
            task.DueDate = dto.DueDate.Value;

        task.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(TaskDto.FromEntity(task));
    }

    /// <summary>
    /// Move a task to a different column (status)
    /// </summary>
    [HttpPatch("tasks/{id:int}/move")]
    public async Task<IActionResult> MoveTask(int id, [FromBody] MoveTaskDto dto)
    {
        var task = await _dbContext.MissionTasks.FindAsync(id);
        if (task == null)
            return NotFound(new { error = "Task not found" });

        task.Status = dto.Status;
        task.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return Ok(TaskDto.FromEntity(task));
    }

    /// <summary>
    /// Delete a task
    /// </summary>
    [HttpDelete("tasks/{id:int}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        var task = await _dbContext.MissionTasks.FindAsync(id);
        if (task == null)
            return NotFound(new { error = "Task not found" });

        _dbContext.MissionTasks.Remove(task);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Get tasks grouped by status (for Kanban board)
    /// </summary>
    [HttpGet("kanban")]
    public async Task<IActionResult> GetKanbanBoard([FromQuery] TaskFilterDto? filter)
    {
        var query = _dbContext.MissionTasks.AsQueryable();

        if (filter?.Type.HasValue == true)
            query = query.Where(t => t.Type == filter.Type.Value);

        if (filter?.Priority.HasValue == true)
            query = query.Where(t => t.Priority == filter.Priority.Value);

        var allTasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        var result = new Dictionary<string, List<TaskDto>>
        {
            ["Backlog"] = allTasks.Where(t => t.Status == TaskStatus.Backlog).Select(TaskDto.FromEntity).ToList(),
            ["Ready"] = allTasks.Where(t => t.Status == TaskStatus.Ready).Select(TaskDto.FromEntity).ToList(),
            ["InProgress"] = allTasks.Where(t => t.Status == TaskStatus.InProgress).Select(TaskDto.FromEntity).ToList(),
            ["Blocked"] = allTasks.Where(t => t.Status == TaskStatus.Blocked).Select(TaskDto.FromEntity).ToList(),
            ["Review"] = allTasks.Where(t => t.Status == TaskStatus.Review).Select(TaskDto.FromEntity).ToList(),
            ["Done"] = allTasks.Where(t => t.Status == TaskStatus.Done).Select(TaskDto.FromEntity).ToList()
        };

        return Ok(result);
    }
}
