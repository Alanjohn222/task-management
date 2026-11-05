using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaskManagement.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class HealthTests(DatabaseFixture database) : IntegrationTest(database)
{
    [Fact]
    public async Task DatabaseHealthReturnsOk()
    {
        using var response = await Client.GetAsync("/health/database");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }
}
