using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaskManagement.Api.IntegrationTests;

public sealed class TestApiFactory(DatabaseFixture database) : WebApplicationFactory<Program>
{
    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TaskDb", database.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(SigningKey));
    }
}
