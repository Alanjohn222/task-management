using System.Net.Http.Json;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SearchAndFilterTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task SearchMatchesTitleAndDescriptionIgnoringCase()
    {
        await SignInAsync(await RegisterUserAsync());
        var titleMatch = await CreateTaskAsync("NEEDLE in title");
        var descriptionMatch = await CreateTaskAsync("Different title", "Contains a NeeDLe");
        await CreateTaskAsync("Unrelated task");

        var page = await GetPageAsync("?search=%20needle%20");
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            new[] { titleMatch.Id, descriptionMatch.Id }.OrderBy(id => id),
            page.Items.Select(task => task.Id).OrderBy(id => id));
    }

    [Theory]
    [InlineData("%", "100% done", "100X done")]
    [InlineData("_", "under_score", "underXscore")]
    [InlineData("'", "O'Reilly", "OReilly")]
    public async Task SearchTreatsSpecialCharactersLiterally(string search, string title, string decoy)
    {
        await SignInAsync(await RegisterUserAsync());
        var expected = await CreateTaskAsync(title);
        await CreateTaskAsync(decoy);
        var page = await GetPageAsync("?search=" + Uri.EscapeDataString(search));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(expected.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task InjectionLikeSearchDoesNotBypassOwnerFilter()
    {
        await SignInAsync(await RegisterUserAsync());
        await CreateTaskAsync("Private task");
        var page = await GetPageAsync("?search=" + Uri.EscapeDataString("' OR 1=1 --"));
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task EmptyAndWhitespaceSearchApplyNoSearchFilter()
    {
        await SignInAsync(await RegisterUserAsync());
        var task = await CreateTaskAsync();
        foreach (var query in new[] { "?search=", "?search=%20%20%20" })
        {
            var page = await GetPageAsync(query);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(task.Id, Assert.Single(page.Items).Id);
        }
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    public async Task PriorityFilterReturnsOnlyMatchingTasks(string priority)
    {
        await SignInAsync(await RegisterUserAsync());
        var tasks = await SeedFilterTasksAsync();
        var page = await GetPageAsync("?priority=" + priority);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            tasks.Where(task => task.Priority == priority).Select(task => task.Id).OrderBy(id => id),
            page.Items.Select(task => task.Id).OrderBy(id => id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionFilterReturnsOnlyMatchingTasks(bool completed)
    {
        await SignInAsync(await RegisterUserAsync());
        var tasks = await SeedFilterTasksAsync();
        var page = await GetPageAsync("?isCompleted=" + completed.ToString().ToLowerInvariant());
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            tasks.Where(task => task.IsCompleted == completed).Select(task => task.Id).OrderBy(id => id),
            page.Items.Select(task => task.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task SearchPriorityAndCompletionFiltersCombineWithAnd()
    {
        await SignInAsync(await RegisterUserAsync());
        var expected = await CreateTaskAsync("Match this", priority: "High");
        await CreateTaskAsync("Match wrong priority", priority: "Low");
        var completed = await CreateTaskAsync("Match completed", priority: "High");
        using var update = await Client.PutAsJsonAsync("/api/tasks/" + completed.Id,
            new { title = completed.Title, isCompleted = true, priority = "High" });
        update.EnsureSuccessStatusCode();
        await CreateTaskAsync("Different title", priority: "High");

        var page = await GetPageAsync("?search=match&priority=High&isCompleted=false");
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(expected.Id, Assert.Single(page.Items).Id);

        var noMatches = await GetPageAsync("?search=absent&priority=High&isCompleted=false");
        Assert.Equal(0, noMatches.TotalCount);
        Assert.Empty(noMatches.Items);
    }

    private async Task<List<TaskResponse>> SeedFilterTasksAsync()
    {
        var tasks = new List<TaskResponse>();
        foreach (var priority in new[] { "Low", "Medium", "High" })
        {
            tasks.Add(await CreateTaskAsync("Open " + priority, priority: priority));
            var completed = await CreateTaskAsync("Complete " + priority, priority: priority);
            using var response = await Client.PutAsJsonAsync("/api/tasks/" + completed.Id,
                new { title = completed.Title, isCompleted = true, priority });
            response.EnsureSuccessStatusCode();
            var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
            Assert.NotNull(updated);
            tasks.Add(updated);
        }
        return tasks;
    }
}
