using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class IncidentPrivilegedAccessTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact] // A-06: Reporter не може виконати triage (403, статус не змінюється)
    public async Task Triage_AsReporterAlice_Returns403_AndDoesNotChangeStatus()
    {
        var title = $"A06-{Guid.NewGuid():N}";
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);

        try
        {
            var incidentId = await CreateNewIncidentAsync(alice, title);

            using var triageResponse = await alice.PostAsJsonAsync(
                $"/api/incidents/{incidentId}/triage",
                new { role = "Analyst", status = "Triaged", isTransitionAllowed = true });

            Assert.Equal(HttpStatusCode.Forbidden, triageResponse.StatusCode);

            var after = await LoadIncidentFromDbAsync(incidentId);
            Assert.Equal(IncidentStatus.New, after!.Status);
        }
        finally
        {
            await DeleteIncidentByTitleAsync(title);
        }
    }

    [Fact] // A-07: Analyst переводить New -> Triaged (204), повторний виклик дає 409
    public async Task Triage_AsAnalystMorgan_OnNewIncident_TransitionsToTriaged_AndSecondCallReturns409()
    {
        var title = $"A07-{Guid.NewGuid():N}";
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);
        using var morgan = await factory.LoginAsync("morgan", StudyPassword.Value);

        try
        {
            var incidentId = await CreateNewIncidentAsync(alice, title);

            using var firstTriage = await morgan.PostAsync($"/api/incidents/{incidentId}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, firstTriage.StatusCode);

            var after = await LoadIncidentFromDbAsync(incidentId);
            Assert.Equal(IncidentStatus.Triaged, after!.Status);

            using var secondTriage = await morgan.PostAsync($"/api/incidents/{incidentId}/triage", null);
            Assert.Equal(HttpStatusCode.Conflict, secondTriage.StatusCode);
        }
        finally
        {
            await DeleteIncidentByTitleAsync(title);
        }
    }

    [Fact] // A-12: child resource і internal comment
    public async Task ChildComments_AsReporterAlice_DeniesBobCommentAndInternalComment()
    {
        using var alice = await factory.LoginAsync("alice", StudyPassword.Value);
        using var morgan = await factory.LoginAsync("morgan", StudyPassword.Value);

        using var bobComment = await alice.GetAsync("/api/incident-comments/30000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.NotFound, bobComment.StatusCode);

        using var internalAsAlice = await alice.GetAsync("/api/incident-comments/30000000-0000-0000-0000-000000000003");
        Assert.Equal(HttpStatusCode.NotFound, internalAsAlice.StatusCode);

        using var detailsAsAlice = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        var aliceDetailsJson = await detailsAsAlice.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Внутрішня нотатка аналітика", aliceDetailsJson, StringComparison.Ordinal);

        using var internalAsMorgan = await morgan.GetAsync("/api/incident-comments/30000000-0000-0000-0000-000000000003");
        Assert.Equal(HttpStatusCode.OK, internalAsMorgan.StatusCode);
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