namespace LiveAuthCore.Models;

using System.ComponentModel.DataAnnotations;

public sealed class CreateProjectRequest
{
    /// <summary>
    /// Project name.
    /// </summary>
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    public required string Name { get; set; }
}
