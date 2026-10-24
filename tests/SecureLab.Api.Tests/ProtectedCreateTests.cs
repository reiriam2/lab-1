using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class ProtectedCreateTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact] // A-01 для create
    public async Task Create_WithoutCookie_Returns401_NotRedirect()
    {
        using var anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var body = new
        {
            title = $"A01-{Guid.NewGuid():N}",
            description = "Спроба створення без входу.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        using var response = await anonymous.PostAsJsonAsync("/api/incidents", body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact] // A-04
    public async Task Create_WithoutOwnerInDto_UsesPrincipalAsOwner()
    {
        var title = $"A04-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", StudyPassword.Value);
        try
        {
            var body = new
            {
                title,
                description = "Опис без поля власника.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            };

            using var response = await bob.PostAsJsonAsync("/api/incidents", body);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await ReadOwnerAsync(response));
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    [Fact] // A-05
    public async Task Create_WithForgedOwnerInJson_KeepsPrincipalAsOwner()
    {
        var title = $"A05-{Guid.NewGuid():N}";
        using var bob = await factory.LoginAsync("bob", StudyPassword.Value);
        try
        {
            var body = new
            {
                title,
                description = "Спроба підробити власника.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                ownerUserId = DbSeeder.AliceId
            };

            using var response = await bob.PostAsJsonAsync("/api/incidents", body);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var owner = await ReadOwnerAsync(response);
            Assert.Equal(DbSeeder.BobId, owner);
            Assert.NotEqual(DbSeeder.AliceId, owner);
        }
        finally
        {
            await DeleteByTitleAsync(title);
        }
    }

    private async Task<Guid> ReadOwnerAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = document.RootElement.GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id);
        return incident.OwnerUserId;
    }

    private async Task DeleteByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }
}