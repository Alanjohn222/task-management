using System.Net;
using System.Net.Http.Json;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class TaskCrudTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task CreateFetchUpdateAndDeleteTask()
    {
        var user = await RegisterUserAsync();
        await SignInAsync(user);

        using var create = await Client.PostAsJsonAsync("/api/tasks", new
        {
            title = "  Prepare demo  ",
            description = "Explain the API",
            dueDate = "2028-02-29",
            priority = "High"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var created = await create.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.EndsWith("/api/tasks/" + created.Id, create.Headers.Location.ToString());
        Assert.Equal("Prepare demo", created.Title);
        Assert.Equal("Explain the API", created.Description);
        Assert.Equal(new DateOnly(2028, 2, 29), created.DueDate);
        Assert.Equal("High", created.Priority);
        Assert.False(created.IsCompleted);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        using var fetch = await Client.GetAsync(create.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        var fetched = await fetch.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Title, fetched.Title);
        Assert.Equal(created.Description, fetched.Description);
        Assert.Equal(created.DueDate, fetched.DueDate);
        Assert.Equal(created.Priority, fetched.Priority);

        var page = await GetPageAsync();
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);

        using var update = await Client.PutAsJsonAsync("/api/tasks/" + created.Id, new
        {
            title = "  Finished demo  ",
            description = "Updated description",
            isCompleted = true,
            dueDate = "2029-01-01",
            priority = "Low"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Finished demo", updated.Title);
        Assert.Equal("Updated description", updated.Description);
        Assert.True(updated.IsCompleted);
        Assert.Equal(new DateOnly(2029, 1, 1), updated.DueDate);
        Assert.Equal("Low", updated.Priority);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);

        var persisted = await Client.GetFromJsonAsync<TaskResponse>("/api/tasks/" + created.Id);
        Assert.NotNull(persisted);
        Assert.Equal(updated.Title, persisted.Title);
        Assert.Equal(updated.Description, persisted.Description);
        Assert.Equal(updated.IsCompleted, persisted.IsCompleted);
        Assert.Equal(updated.DueDate, persisted.DueDate);
        Assert.Equal(updated.Priority, persisted.Priority);
        Assert.Equal(updated.UpdatedAt, persisted.UpdatedAt);

        using var delete = await Client.DeleteAsync("/api/tasks/" + created.Id);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await delete.Content.ReadAsByteArrayAsync());
        using var afterDelete = await Client.GetAsync("/api/tasks/" + created.Id);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        Assert.Empty((await GetPageAsync()).Items);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task MissingTaskReturnsNotFound(string method)
    {
        await SignInAsync(await RegisterUserAsync());
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/tasks/" + Guid.NewGuid());
        if (method == "PUT")
            request.Content = JsonContent.Create(new { title = "Missing", isCompleted = false });
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
