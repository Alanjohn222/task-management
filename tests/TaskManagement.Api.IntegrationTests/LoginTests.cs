using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class LoginTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task CorrectCredentialsReturnTokenNameAndUserIdentity()
    {
        var user = await RegisterUserAsync();
        using var response = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email.ToUpperInvariant(), password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Name, body.GetProperty("name").GetString());
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.True(body.GetProperty("expiresAt").GetDateTime() > DateTime.UtcNow);

        // Also send the token through the real bearer middleware.
        Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var identity = await Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        var identityBody = await identity.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id, identityBody.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmailReturnTheSameUnauthorizedMessage()
    {
        var user = await RegisterUserAsync();
        using var wrongPassword = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = "WrongPassword123!" });
        using var unknownUser = await Client.PostAsJsonAsync("/api/auth/login",
            new { email = $"unknown-{Guid.NewGuid():N}@example.com", password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    public async Task MissingFieldReturnsBadRequest(string field)
    {
        var request = new Dictionary<string, string>
        {
            ["email"] = "test@example.com",
            ["password"] = "TestPassword123!"
        };
        request.Remove(field);
        using var response = await Client.PostAsJsonAsync("/api/auth/login", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", "TestPassword123!")]
    [InlineData("invalid", "TestPassword123!")]
    [InlineData("test@example.com", "")]
    public async Task InvalidInputReturnsBadRequest(string email, string password)
    {
        using var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
