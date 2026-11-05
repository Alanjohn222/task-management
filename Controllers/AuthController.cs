using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Contracts;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace TaskManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{

    private sealed class LoginUser
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
    }


        //register
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        [FromServices] IConfiguration configuration,
        [FromServices] IPasswordHasher<string> passwordHasher,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var passwordHash = passwordHasher.HashPassword(email, request.Password);

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO users (name, email, password_hash)
            VALUES (@Name, @Email, @PasswordHash)
            ON CONFLICT (email) DO NOTHING
            RETURNING id;
            """;

        var command = new CommandDefinition(
            sql,
            new { Name = name, Email = email, PasswordHash = passwordHash },
            cancellationToken: cancellationToken);

        var userId = await connection.QuerySingleOrDefaultAsync<Guid?>(command);

        if (userId is null)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        return StatusCode(StatusCodes.Status201Created, new { id = userId.Value, name, email });
    }


    //Login
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IConfiguration configuration,
        [FromServices] IPasswordHasher<string> passwordHasher,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var connectionString = configuration.GetConnectionString("TaskDb")
            ?? throw new InvalidOperationException("The TaskDb connection string is missing.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT id, name, password_hash AS "PasswordHash"
            FROM users
            WHERE email = @Email;
            """;

        var command = new CommandDefinition(
            sql,
            new { Email = email },
            cancellationToken: cancellationToken);

        var user = await connection.QuerySingleOrDefaultAsync<LoginUser>(command);

        if (user is null ||
            passwordHasher.VerifyHashedPassword(email, user.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var signingKey = configuration["Jwt:SigningKey"]
        ?? throw new InvalidOperationException("The JWT signing key is missing.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddMinutes(1500);

        var token = new JwtSecurityToken(
            issuer: "TaskManagement.Api",
            audience: "TaskManagement.Api",
            claims: new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()) },
            expires: expiresAt,
            signingCredentials: credentials);

        return Ok(new
        {
            accessToken = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            name = user.Name
        });
    }






    //test
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        return Ok(new { id = userId });
    }
}