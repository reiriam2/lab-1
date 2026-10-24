using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public sealed class LoginRateLimitTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Login_ExceedingLimit_Returns429_ThenRecoversAfterWindow()
    {
        // Окремий host: лічильники належать host, а не HttpClient.
        using var host = factory.WithWebHostBuilder(_ => { });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N");

        for (var i = 0; i < 10; i++)
        {
            using var attempt = await client.PostAsJsonAsync(
                "/api/auth/login", new { userName = "alice", password = wrongPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = "alice", password = wrongPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var text = await limited.Content.ReadAsStringAsync();
        Assert.DoesNotContain("alice", text, StringComparison.OrdinalIgnoreCase);

        // Вікно 15 с + запас.
        await Task.Delay(TimeSpan.FromSeconds(16));

        using var recovered = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = "alice", password = StudyPassword.Value });
        Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
    }
}