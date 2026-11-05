using System.ComponentModel.DataAnnotations;

namespace TaskManagement.Api.Contracts;

public sealed class UpdateTaskRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    public required bool IsCompleted { get; init; }
    public DateOnly? DueDate { get; init; }

    [Required]
    [AllowedValues("Low", "Medium", "High")]
    public string Priority { get; init; } = "Medium";
}
