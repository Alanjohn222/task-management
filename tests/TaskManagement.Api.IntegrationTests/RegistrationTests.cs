using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class RegistrationTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task RegisterWithValidDetailsReturnsCreated()
    {
        var email = $"test-{Guid.NewGuid():N}@example.com";
        const string password = "TestPassword123!";
        using var response = await Client.PostAsJsonAsync("/api/auth/register",
            new { name = "  Test User  ", email, password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = user.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Test User", user.GetProperty("name").GetString());
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.False(user.TryGetProperty("password", out _));
        Assert.False(user.TryGetProperty("passwordHash", out _));

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT password_hash FROM users WHERE id = @Id", connection);
        command.Parameters.AddWithValue("Id", id);
        var storedHash = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.NotEmpty(storedHash);
        Assert.NotEqual(password, storedHash);
    }

    [Fact]
    public async Task RegisterWithDuplicateEmailReturnsConflict()
    {
        var request = new
        {
            name = "Duplicate Email Test",
            email = $"duplicate-{Guid.NewGuid():N}@example.com",
            password = "TestPassword123!"
        };
        using var first = await Client.PostAsJsonAsync("/api/auth/register", request);
        using var second = await Client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task DuplicateEmailIsCaseInsensitive()
    {
        var user = await RegisterUserAsync();
        using var response = await Client.PostAsJsonAsync("/api/auth/register",
            new { name = user.Name, email = user.Email.ToUpperInvariant(), password = user.Password });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("password")]
    public async Task MissingRequiredFieldReturnsBadRequest(string field)
    {
        var request = ValidRequest();
        request.Remove(field);
        using var response = await Client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object?[]> InvalidRegistrationValues()
    {
        yield return ["name", null];
        yield return ["name", ""];
        yield return ["name", "   "];
        yield return ["name", new string('n', 101)];
        yield return ["email", null];
        yield return ["email", ""];
        yield return ["email", "invalid-email"];
        yield return ["email", new string('a', 243) + "@example.com"];
        yield return ["password", null];
        yield return ["password", ""];
        yield return ["password", "     "];
        yield return ["password", "short"];
        yield return ["password", new string('p', 129)];
    }

    [Theory]
    [MemberData(nameof(InvalidRegistrationValues))]
    public async Task InvalidFieldReturnsBadRequest(string field, object? value)
    {
        var request = ValidRequest();
        request[field] = value;
        using var response = await Client.PostAsJsonAsync("/api/auth/register", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Dictionary<string, object?> ValidRequest() => new()
    {
        ["name"] = "Test User",
        ["email"] = $"validation-{Guid.NewGuid():N}@example.com",
        ["password"] = "TestPassword123!"
    };
}
