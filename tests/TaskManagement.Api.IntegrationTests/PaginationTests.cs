using System.Net;
using System.Net.Http.Json;
using Npgsql;
using TaskManagement.Api.Contracts;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class PaginationTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task DefaultPagesHaveStableOrderingWithoutDuplicatesOrGaps()
    {
        var user = await RegisterUserAsync();
        await SignInAsync(user);
        var tasks = new List<TaskResponse>();
        for (var number = 0; number < 45; number++)
            tasks.Add(await CreateTaskAsync("Page task " + number));

        // Force ties to exercise the ID tie-breaker rather than relying on timing.
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE tasks SET created_at = @CreatedAt WHERE owner_id = @OwnerId", connection);
        command.Parameters.AddWithValue("CreatedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue("OwnerId", user.Id);
        await command.ExecuteNonQueryAsync();

        var expected = tasks.Select(task => task.Id.ToString())
            .OrderByDescending(id => id, StringComparer.Ordinal).ToArray();
        var first = await GetPageAsync();
        Assert.Equal(1, first.Page);
        Assert.Equal(20, first.PageSize);
        Assert.Equal(45, first.TotalCount);
        Assert.Equal(expected.Take(20), first.Items.Select(task => task.Id.ToString()));

        var second = await GetPageAsync("?page=2");
        var third = await GetPageAsync("?page=3");
        Assert.Equal(2, second.Page);
        Assert.Equal(3, third.Page);
        Assert.Equal(45, second.TotalCount);
        Assert.Equal(45, third.TotalCount);
        Assert.Equal(20, second.Items.Count());
        Assert.Equal(5, third.Items.Count());
        var combined = first.Items.Concat(second.Items).Concat(third.Items)
            .Select(task => task.Id.ToString()).ToArray();
        Assert.Equal(expected, combined);
        Assert.Equal(45, combined.Distinct().Count());

        var repeat = await GetPageAsync();
        Assert.Equal(first.Items.Select(task => task.Id), repeat.Items.Select(task => task.Id));
        var beyond = await GetPageAsync("?page=4");
        Assert.Empty(beyond.Items);
        Assert.Equal(45, beyond.TotalCount);
        var maximumSize = await GetPageAsync("?pageSize=100");
        Assert.Equal(45, maximumSize.Items.Count());
        Assert.Equal(100, maximumSize.PageSize);
        var hugeOffset = await GetPageAsync("?page=2147483647&pageSize=100");
        Assert.Empty(hugeOffset.Items);
        Assert.Equal(45, hugeOffset.TotalCount);
    }

    [Fact]
    public async Task NewestTaskAppearsFirst()
    {
        await SignInAsync(await RegisterUserAsync());
        var older = await CreateTaskAsync("Older");
        var newer = await CreateTaskAsync("Newer");
        Assert.True(newer.CreatedAt > older.CreatedAt);
        var page = await GetPageAsync();
        Assert.Equal(new[] { newer.Id, older.Id }, page.Items.Select(task => task.Id));
    }

    [Fact]
    public async Task EmptyAccountReturnsEmptyPageAndZeroTotal()
    {
        await SignInAsync(await RegisterUserAsync());
        var page = await GetPageAsync();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task CountAndPagesRespectAllFilters()
    {
        await SignInAsync(await RegisterUserAsync());
        var expected = new List<Guid>();
        for (var number = 0; number < 12; number++)
        {
            var priority = number % 2 == 0 ? "High" : "Low";
            var completed = number % 3 == 0;
            var task = await CreateTaskAsync("Filter target " + number, priority: priority);
            if (completed)
            {
                using var response = await Client.PutAsJsonAsync("/api/tasks/" + task.Id,
                    new { title = task.Title, isCompleted = true, priority });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            if (priority == "High" && !completed) expected.Add(task.Id);
        }
        await CreateTaskAsync("Unrelated", priority: "High");
        const string filter = "?search=filter&isCompleted=false&priority=High&pageSize=3";
        var first = await GetPageAsync(filter + "&page=1");
        var second = await GetPageAsync(filter + "&page=2");
        Assert.Equal(expected.Count, first.TotalCount);
        Assert.Equal(expected.Count, second.TotalCount);
        Assert.Equal(3, first.Items.Count());
        Assert.Single(second.Items);
        Assert.Equal(
            expected.OrderBy(id => id),
            first.Items.Concat(second.Items).Select(task => task.Id).OrderBy(id => id));
        var beyond = await GetPageAsync(filter + "&page=3");
        Assert.Empty(beyond.Items);
        Assert.Equal(expected.Count, beyond.TotalCount);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?page=abc")]
    [InlineData("?page=2147483648")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=-1")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=abc")]
    [InlineData("?priority=Urgent")]
    [InlineData("?priority=high")]
    [InlineData("?isCompleted=maybe")]
    public async Task InvalidQueryReturnsBadRequest(string query)
    {
        await SignInAsync(await RegisterUserAsync());
        using var response = await Client.GetAsync("/api/tasks" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
