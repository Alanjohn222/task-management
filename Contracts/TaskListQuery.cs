using System.ComponentModel.DataAnnotations;

namespace TaskManagement.Api.Contracts;

public sealed class TaskListQuery
{
    public string? Search { get; init; }

    public bool? IsCompleted { get; init; }

    [AllowedValues(null, "Low", "Medium", "High")]
    public string? Priority { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
