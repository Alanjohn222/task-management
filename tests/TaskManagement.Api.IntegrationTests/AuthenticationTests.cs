using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AuthenticationTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/tasks")]
    [InlineData("POST", "/api/tasks")]
    [InlineData("GET", "/api/tasks/11111111-1111-1111-1111-111111111111")]
    [InlineData("PUT", "/api/tasks/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/tasks/11111111-1111-1111-1111-111111111111")]
    public async Task ProtectedEndpointWithoutTokenReturnsUnauthorized(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { title = "Test", isCompleted = false });
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("tampered")]
    public async Task InvalidTokenReturnsUnauthorized(string kind)
    {
        var user = await RegisterUserAsync();
        var expires = DateTime.UtcNow.AddMinutes(5);
        if (kind == "expired") expires = DateTime.UtcNow.AddMinutes(-1);

        var jwt = new JwtSecurityToken(
            issuer: "TaskManagement.Api",
            audience: "TaskManagement.Api",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())],
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Factory.SigningKey), SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (kind == "tampered")
        {
            var parts = token.Split('.');
            parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
            token = string.Join('.', parts);
        }

        Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await Client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
