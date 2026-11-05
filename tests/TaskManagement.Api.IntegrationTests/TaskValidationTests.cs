using System.Net;
using System.Net.Http.Json;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class TaskValidationTests(DatabaseFixture database) : IntegrationTest(database)
{
    public static IEnumerable<object?[]> InvalidTaskValues()
    {
        yield return ["title", null];
        yield return ["title", ""];
        yield return ["title", "   "];
        yield return ["title", new string('t', 201)];
        yield return ["description", new string('d', 2001)];
        yield return ["priority", null];
        yield return ["priority", ""];
        yield return ["priority", "Urgent"];
        yield return ["priority", "high"];
        yield return ["priority", 1];
        yield return ["dueDate", "2027-02-29"];
        yield return ["dueDate", "2026-13-01"];
        yield return ["dueDate", "2026-01-01T12:00:00Z"];
        yield return ["dueDate", ""];
    }

    [Theory]
    [MemberData(nameof(InvalidTaskValues))]
    public async Task InvalidCreateReturnsBadRequestWithoutSavingTask(string field, object? value)
    {
        await SignInAsync(await RegisterUserAsync());
        var request = new Dictionary<string, object?> { ["title"] = "Valid task" };
        request[field] = value;
        using var response = await Client.PostAsJsonAsync("/api/tasks", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await GetPageAsync()).Items);
    }

    [Theory]
    [MemberData(nameof(InvalidTaskValues))]
    public async Task InvalidUpdateReturnsBadRequestWithoutChangingTask(string field, object? value)
    {
        await SignInAsync(await RegisterUserAsync());
        var task = await CreateTaskAsync("Original", "Keep this", "High", "2028-02-29");
        var request = new Dictionary<string, object?>
        {
            ["title"] = "Changed",
            ["isCompleted"] = true
        };
        request[field] = value;
        using var response = await Client.PutAsJsonAsync("/api/tasks/" + task.Id, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await Client.GetFromJsonAsync<TaskResponse>("/api/tasks/" + task.Id);
        Assert.NotNull(unchanged);
        Assert.Equal(task.Title, unchanged.Title);
        Assert.Equal(task.Description, unchanged.Description);
        Assert.Equal(task.IsCompleted, unchanged.IsCompleted);
        Assert.Equal(task.DueDate, unchanged.DueDate);
        Assert.Equal(task.Priority, unchanged.Priority);
        Assert.Equal(task.UpdatedAt, unchanged.UpdatedAt);
    }

    [Theory]
    [InlineData("POST", "title")]
    [InlineData("PUT", "title")]
    [InlineData("PUT", "isCompleted")]
    public async Task MissingRequiredTaskFieldReturnsBadRequest(string method, string field)
    {
        await SignInAsync(await RegisterUserAsync());
        var task = await CreateTaskAsync();
        var body = new Dictionary<string, object?> { ["title"] = "Title", ["isCompleted"] = false };
        body.Remove(field);
        using var request = new HttpRequestMessage(new HttpMethod(method),
            method == "POST" ? "/api/tasks" : "/api/tasks/" + task.Id)
        {
            Content = JsonContent.Create(body)
        };
        using var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OmittedCreateFieldsUseDefaults()
    {
        await SignInAsync(await RegisterUserAsync());
        using var response = await Client.PostAsJsonAsync("/api/tasks", new { title = "Defaults" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
        Assert.Equal("Medium", task.Priority);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public async Task OmittedUpdateFieldsResetToDefaults()
    {
        await SignInAsync(await RegisterUserAsync());
        var original = await CreateTaskAsync("Original", "Description", "High", "2028-02-29");
        using var response = await Client.PutAsJsonAsync("/api/tasks/" + original.Id,
            new { title = "Reset defaults", isCompleted = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        Assert.Null(task.Description);
        Assert.Null(task.DueDate);
        Assert.Equal("Medium", task.Priority);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    public async Task ValidPriorityAndLeapDateRoundTrip(string priority)
    {
        await SignInAsync(await RegisterUserAsync());
        var created = await CreateTaskAsync(priority: priority, dueDate: "2028-02-29");
        var fetched = await Client.GetFromJsonAsync<TaskResponse>("/api/tasks/" + created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(priority, fetched.Priority);
        Assert.Equal(new DateOnly(2028, 2, 29), fetched.DueDate);
        var listed = Assert.Single((await GetPageAsync()).Items);
        Assert.Equal(priority, listed.Priority);
        Assert.Equal(fetched.DueDate, listed.DueDate);
    }

    [Fact]
    public async Task ExplicitNullClearsDueDateAndBoundaryLengthsAreAccepted()
    {
        await SignInAsync(await RegisterUserAsync());
        var title = new string('t', 200);
        var description = new string('d', 2000);
        var created = await CreateTaskAsync(title, description, dueDate: "2000-01-01");
        using var response = await Client.PutAsJsonAsync("/api/tasks/" + created.Id,
            new { title, description, isCompleted = false, dueDate = (string?)null, priority = "Low" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(updated);
        Assert.Equal(title, updated.Title);
        Assert.Equal(description, updated.Description);
        Assert.Null(updated.DueDate);
        Assert.Equal("Low", updated.Priority);
    }
}
