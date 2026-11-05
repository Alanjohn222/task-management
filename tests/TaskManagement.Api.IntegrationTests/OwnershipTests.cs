using System.Net;
using System.Net.Http.Json;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class OwnershipTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task OtherUserCannotListFetchUpdateOrDeleteOwnersTask()
    {
        var owner = await RegisterUserAsync();
        await SignInAsync(owner);
        var task = await CreateTaskAsync("Owner private task", "Private description", "High", "2028-02-29");

        var otherUser = await RegisterUserAsync();
        using var otherClient = Factory.CreateClient();
        await SignInAsync(otherUser, otherClient);
        var otherTask = await CreateTaskAsync("Other account task", client: otherClient);

        var otherPage = await GetPageAsync(client: otherClient);
        Assert.Equal(1, otherPage.TotalCount);
        Assert.Equal(otherTask.Id, Assert.Single(otherPage.Items).Id);
        Assert.Empty((await GetPageAsync("?search=Owner", otherClient)).Items);

        using var fetch = await otherClient.GetAsync("/api/tasks/" + task.Id);
        using var update = await otherClient.PutAsJsonAsync("/api/tasks/" + task.Id,
            new { title = "Stolen", isCompleted = true, priority = "Low" });
        using var delete = await otherClient.DeleteAsync("/api/tasks/" + task.Id);
        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var unchanged = await Client.GetFromJsonAsync<TaskResponse>("/api/tasks/" + task.Id);
        Assert.NotNull(unchanged);
        Assert.Equal(task.Title, unchanged.Title);
        Assert.Equal(task.Description, unchanged.Description);
        Assert.Equal(task.IsCompleted, unchanged.IsCompleted);
        Assert.Equal(task.Priority, unchanged.Priority);
        Assert.Equal(task.DueDate, unchanged.DueDate);
        Assert.Equal(task.UpdatedAt, unchanged.UpdatedAt);
        Assert.Equal(task.Id, Assert.Single((await GetPageAsync()).Items).Id);
    }
}
