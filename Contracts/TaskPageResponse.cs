namespace TaskManagement.Api.Contracts;

public sealed class TaskPageResponse
{
    public required IEnumerable<TaskResponse> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
}
