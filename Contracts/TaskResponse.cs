namespace TaskManagement.Api.Contracts;

public sealed class TaskResponse
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateOnly? DueDate { get; init; }
    public string Priority { get; init; } = "Medium";
}
