using System.IdentityModel.Tokens.Jwt;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tasks")]
public sealed class TasksController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] TaskListQuery query,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string filter = """
            WHERE owner_id = @OwnerId
              AND (CAST(@Search AS text) IS NULL
                   OR strpos(lower(title), lower(@Search)) > 0
                   OR strpos(lower(description), lower(@Search)) > 0)
              AND (CAST(@IsCompleted AS boolean) IS NULL OR is_completed = @IsCompleted)
              AND (CAST(@Priority AS text) IS NULL OR priority = @Priority)
            """;

        var sql = $"""
            SELECT count(*)
            FROM tasks
            {filter};

            SELECT id, title, description, due_date AS "DueDate", priority,
                   is_completed AS "IsCompleted",
                   created_at AS "CreatedAt",
                   updated_at AS "UpdatedAt"
            FROM tasks
            {filter}
            ORDER BY created_at DESC, id DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var search = query.Search?.Trim();

        // Both result sets must describe the same database snapshot.
        await using var transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);

        var command = new CommandDefinition(
            sql,
            new
            {
                OwnerId = userId,
                Search = string.IsNullOrEmpty(search) ? null : search,
                query.IsCompleted,
                query.Priority,
                query.PageSize,
                Offset = ((long)query.Page - 1) * query.PageSize
            },
            transaction: transaction,
            cancellationToken: cancellationToken);

        using var results = await connection.QueryMultipleAsync(command);
        var totalCount = await results.ReadSingleAsync<long>();
        var tasks = (await results.ReadAsync<TaskResponse>()).ToArray();
        await transaction.CommitAsync(cancellationToken);

        return Ok(new TaskPageResponse
        {
            Items = tasks,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount
        });
    }


    //Create a task
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTaskRequest request,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO tasks (owner_id, title, description, due_date, priority)
            VALUES (@OwnerId, @Title, @Description, @DueDate, @Priority)
            RETURNING id, title, description, due_date AS "DueDate", priority,
                    is_completed AS "IsCompleted",
                    created_at AS "CreatedAt",
                    updated_at AS "UpdatedAt";
            """;

        var command = new CommandDefinition(
            sql,
            new
            {
                OwnerId = userId,
                Title = request.Title.Trim(),
                Description = request.Description,
                DueDate = request.DueDate,
                Priority = request.Priority
            },
            cancellationToken: cancellationToken);

        var task = await connection.QuerySingleAsync<TaskResponse>(command);

        return CreatedAtAction(nameof(GetById), new { id = task.Id }, task);
    }

    //Get a single task
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid id,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, title, description, due_date AS "DueDate", priority,
                is_completed AS "IsCompleted",
                created_at AS "CreatedAt",
                updated_at AS "UpdatedAt"
            FROM tasks
            WHERE id = @Id AND owner_id = @OwnerId;
            """;

        var command = new CommandDefinition(
            sql,
            new { Id = id, OwnerId = userId },
            cancellationToken: cancellationToken);

        var task = await connection.QuerySingleOrDefaultAsync<TaskResponse>(command);

        if (task is null)
        {
            return NotFound();
        }

        return Ok(task);
    }

    //Update a task
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateTaskRequest request,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE tasks
            SET title = @Title,
                description = @Description,
                due_date = @DueDate,
                priority = @Priority,
                is_completed = @IsCompleted,
                updated_at = now()
            WHERE id = @Id AND owner_id = @OwnerId
            RETURNING id, title, description, due_date AS "DueDate", priority,
                    is_completed AS "IsCompleted",
                    created_at AS "CreatedAt",
                    updated_at AS "UpdatedAt";
            """;

        var command = new CommandDefinition(
            sql,
            new
            {
                Id = id,
                OwnerId = userId,
                Title = request.Title.Trim(),
                Description = request.Description,
                DueDate = request.DueDate,
                Priority = request.Priority,
                IsCompleted = request.IsCompleted
            },
            cancellationToken: cancellationToken);

        var task = await connection.QuerySingleOrDefaultAsync<TaskResponse>(command);

        if (task is null)
        {
            return NotFound();
        }

        return Ok(task);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DELETE FROM tasks
            WHERE id = @Id AND owner_id = @OwnerId;
            """;

        var command = new CommandDefinition(
            sql,
            new { Id = id, OwnerId = userId },
            cancellationToken: cancellationToken);

        var deleted = await connection.ExecuteAsync(command);

        if (deleted == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}
