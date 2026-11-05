using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

// Each test gets its own API host and HTTP client. The collection shares only PostgreSQL.
public abstract class IntegrationTest : IDisposable
{
    protected readonly TestApiFactory Factory;
    protected readonly HttpClient Client;
    protected readonly DatabaseFixture Database;

    protected IntegrationTest(DatabaseFixture database)
    {
        Database = database;
        Factory = new TestApiFactory(database);
        Client = Factory.CreateClient();
    }

    protected sealed record TestUser(Guid Id, string Name, string Email, string Password);

    protected async Task<TestUser> RegisterUserAsync()
    {
        var name = "Integration Test User";
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var password = $"Test-{Guid.NewGuid():N}!";
        using var response = await Client.PostAsJsonAsync(
            "/api/auth/register", new { name, email, password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestUser(body.GetProperty("id").GetGuid(), name, email, password);
    }

    protected async Task<string> SignInAsync(TestUser user, HttpClient? client = null)
    {
        client ??= Client;
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return token!;
    }

    protected async Task<TaskResponse> CreateTaskAsync(
        string title = "Test task",
        string? description = null,
        string priority = "Medium",
        string? dueDate = null,
        HttpClient? client = null)
    {
        client ??= Client;
        using var response = await client.PostAsJsonAsync(
            "/api/tasks", new { title, description, priority, dueDate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        return task;
    }

    protected async Task<TaskPageResponse> GetPageAsync(string query = "", HttpClient? client = null)
    {
        client ??= Client;
        using var response = await client.GetAsync("/api/tasks" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<TaskPageResponse>();
        Assert.NotNull(page);
        return page;
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
