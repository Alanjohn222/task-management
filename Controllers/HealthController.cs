using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;

namespace TaskManagement.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok" });
    }


    [HttpGet("database")]
public async Task<IActionResult> GetDatabase(
    [FromServices] IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var connectionString = configuration.GetConnectionString("TaskDb")
        ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);

    var command = new CommandDefinition(
        "SELECT current_database() = @ExpectedDatabase;",
        new { ExpectedDatabase = "task_management" },
        cancellationToken: cancellationToken);

    var isExpectedDatabase = await connection.QuerySingleAsync<bool>(command);

    return isExpectedDatabase
        ? Ok(new { status = "ok" })
        : StatusCode(503, new { status = "unexpected_database" });
}
}
