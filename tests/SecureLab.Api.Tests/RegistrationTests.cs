using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class RegistrationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private HttpClient NewClient() => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static object Body(string userName, string password) => new
    {
        userName,
        email = userName + "@example.test",
        displayName = "Тест Реєстрації",
        password
    };

    [Fact] // T-01 і duplicate-контракт
    public async Task Register_Valid_Returns201_ThenDuplicate_Returns409()
    {
        var userName = "reg" + Guid.NewGuid().ToString("N")[..8];
        var password = "Aa1!" + Guid.NewGuid().ToString("N");
        using var client = NewClient();
        try
        {
            using var created = await client.PostAsJsonAsync(
                "/api/auth/register", Body(userName, password));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using (var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            {
                var root = document.RootElement;
                Assert.Equal(userName, root.GetProperty("userName").GetString());
                foreach (var forbidden in new[]
                         { "password", "passwordHash", "securityStamp", "token", "cookie", "role" })
                    Assert.False(root.TryGetProperty(forbidden, out _));
            }

            using var duplicate = await client.PostAsJsonAsync(
                "/api/auth/register", Body(userName, password));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("application/problem+json",
                duplicate.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await DeleteUserAsync(userName);
        }
    }

    [Fact] // T-02: некоректний запит
    public async Task Register_WeakPassword_Returns400WithPasswordKey()
    {
        var userName = "reg" + Guid.NewGuid().ToString("N")[..8];
        using var client = NewClient();
        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/register", Body(userName, "short"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json",
                response.Content.Headers.ContentType?.MediaType);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("errors")
                .TryGetProperty("password", out _));
        }
        finally
        {
            await DeleteUserAsync(userName);
        }
    }

    private async Task DeleteUserAsync(string userName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName);
        if (user is not null) await users.DeleteAsync(user);
    }
}