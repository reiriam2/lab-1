using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class AuthenticationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact] // A-01
    public async Task Me_WithoutCookie_Returns401_NotRedirect()
    {
        using var anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await anonymous.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact] // A-02
    public async Task Me_AfterLogin_ReturnsSafeProfileOfAlice()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword.Value);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(DbSeeder.AliceId, root.GetProperty("userId").GetGuid());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        foreach (var forbidden in new[]
                 { "password", "passwordHash", "securityStamp", "token", "cookie" })
            Assert.False(root.TryGetProperty(forbidden, out _));
    }

    [Fact] // A-03
    public async Task Me_WithForeignDemoHeader_StillReturnsAlice()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword.Value);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId, document.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact] // A-06
    public async Task Me_AfterLogout_Returns401()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword.Value);
        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact] // A-07
    public async Task Login_UnknownUserAndWrongPassword_LookTheSame()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var wrongPassword = "Wrong-" + Guid.NewGuid().ToString("N");
        var unknownName = "nobody" + Guid.NewGuid().ToString("N")[..8];

        using var unknown = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = unknownName, password = wrongPassword });
        using var wrong = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = "alice", password = wrongPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(unknown.Content.Headers.ContentType?.MediaType,
            wrong.Content.Headers.ContentType?.MediaType);
        // traceId навмисно не порівнюємо: він різний у кожного запиту.
        Assert.Equal(await ProblemShape.ReadAsync(unknown), await ProblemShape.ReadAsync(wrong));
    }
}