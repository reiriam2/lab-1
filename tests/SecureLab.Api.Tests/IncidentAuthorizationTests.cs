using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class IncidentAuthorizationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact] // A-01: Анонімний доступ до захищених маршрутів повертає 401 без редиректу
    public async Task Anonymous_ProtectedEndpoints_Return401_NotRedirect()
    {
        using var anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var details = await anonymous.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.Unauthorized, details.StatusCode);
        Assert.Null(details.Headers.Location);

        using var list = await anonymous.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact] // A-02: Власник (Alice) успішно читає свій інцидент
    public async Task GetDetails_AsOwnerAlice_Returns200AndOwnIncident()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceIncidentId, doc.RootElement.GetProperty("id").GetGuid());
    }

    [Fact] // A-03 та A-10: Чужий ресурс для Alice повертає 404, нерозрізнюваний від неіснуючого ID
    public async Task GetDetails_AsNonOwnerAlice_ReturnsSame404AsUnknownId_AndHidesBobData()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);

        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());

        var foreignText = await foreign.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Невідома спроба входу", foreignText, StringComparison.Ordinal);
        Assert.DoesNotContain("Боб Мельник", foreignText, StringComparison.Ordinal);

        var foreignBody = JsonNode.Parse(foreignText)!.AsObject();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonObject>();

        foreignBody.Remove("traceId");
        unknownBody!.Remove("traceId");

        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));
    }

    [Fact] // A-03R: Morgan (Analyst) читає чужий інцидент Alice для аналізу (включно з internal comment), але не може його змінювати
    public async Task GetDetails_AsAnalystMorgan_AllowsReadWithInternalComments_ButDeniesUpdate()
    {
        using var morgan = await factory.LoginAsync("morgan", StudyPassword.Value);

        using var readResponse = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var readJson = await readResponse.Content.ReadAsStringAsync();
        Assert.Contains("Внутрішня нотатка аналітика", readJson, StringComparison.Ordinal);

        using var updateResponse = await morgan.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}",
            new { title = "Morgan edit attempt", description = "Analyst cannot edit content." });

        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
    }

    [Fact] // A-04: Спроба Alice змінити або видалити інцидент Bob відхиляється (404) і не змінює стан у БД
    public async Task UpdateAndDelete_AsNonOwnerAlice_AreDenied_AndDoNotChangeBobResource()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);
        var before = await LoadIncidentFromDbAsync(DbSeeder.BobIncidentId);

        using var updateResponse = await alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}",
            new
            {
                title = "Tampered title by Alice",
                description = "Цей опис має щонайменше сорок символів для проходження перевірки довжини."
            });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        using var deleteResponse = await alice.DeleteAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        var after = await LoadIncidentFromDbAsync(DbSeeder.BobIncidentId);
        Assert.NotNull(after);
        Assert.Equal(before!.Title, after!.Title);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.OwnerUserId, after.OwnerUserId);
        Assert.Equal(before.Status, after.Status);
    }

    [Fact] // A-05: Список інцидентів для Alice містить лише її власні записи
    public async Task GetList_AsAlice_ReturnsOnlyAliceIncidents()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);
        using var response = await alice.GetAsync("/api/incidents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();

        Assert.NotEmpty(ids);
        Assert.Contains(DbSeeder.AliceIncidentId, ids);
        Assert.DoesNotContain(DbSeeder.BobIncidentId, ids);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var owners = await db.Incidents.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => item.OwnerUserId)
            .Distinct()
            .ToListAsync();

        Assert.Single(owners, DbSeeder.AliceId);
    }

    [Fact] // A-08a: Overposting при звичайному update змінює лише контент і не змінює OwnerUserId чи Status
    public async Task Update_WithForgedOwnerRoleAndStatus_UpdatesContentOnly_AndKeepsProtectedState()
    {
        var title = $"A08a-{Guid.NewGuid():N}";
        var updatedTitle = $"A08a-Updated-{Guid.NewGuid():N}";
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);

        try
        {
            var incidentId = await CreateNewIncidentAsync(alice, title);

            using var response = await alice.PutAsJsonAsync(
                $"/api/incidents/{incidentId}",
                new
                {
                    title = updatedTitle,
                    description = "Оновлений дозволений опис інциденту.",
                    ownerUserId = DbSeeder.BobId,
                    role = DbSeeder.AdministratorRole,
                    status = "Closed"
                });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var after = await LoadIncidentFromDbAsync(incidentId);
            Assert.Equal(updatedTitle, after!.Title);
            Assert.Equal("Оновлений дозволений опис інциденту.", after.Description);
            Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
            Assert.Equal(IncidentStatus.New, after.Status);
        }
        finally
        {
            await DeleteIncidentByTitleAsync(title);
            await DeleteIncidentByTitleAsync(updatedTitle);
        }
    }

    private static async Task<Guid> CreateNewIncidentAsync(HttpClient client, string title)
    {
        using var created = await client.PostAsJsonAsync("/api/incidents", new
        {
            title,
            description = "Початковий опис тестового інциденту.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var doc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Incident?> LoadIncidentFromDbAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
    }

    private async Task DeleteIncidentByTitleAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var ids = await db.Incidents.Where(item => item.Title == title).Select(item => item.Id).ToListAsync();
        if (ids.Count > 0)
        {
            await db.IncidentStatusHistory.Where(h => ids.Contains(h.IncidentId)).ExecuteDeleteAsync();
            await db.IncidentComments.Where(c => ids.Contains(c.IncidentId)).ExecuteDeleteAsync();
            await db.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
        }
    }
}