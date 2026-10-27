using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/triage", TriageAsync)
            .WithName("TriageIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/incident-comments/{id:guid}", GetCommentAsync)
            .WithTags("IncidentComments")
            .WithName("GetIncidentComment")
            .RequireAuthorization()
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out var userId))
            return Results.Challenge();

        IncidentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var isKnownStatus = Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                && Enum.IsDefined(candidate);

            if (!isKnownStatus)
            {
                return Results.Problem(
                    title: "Некоректний фільтр статусу",
                    detail: "Використайте одне зі значень: New, Triaged, InProgress, Resolved, Closed.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            parsedStatus = candidate;
        }

        var isAnalyst = IncidentAccess.IsAnalyst(httpContext.User);
        var isReporter = IncidentAccess.IsReporter(httpContext.User);

        return Results.Ok(await queries.GetListAsync(
            parsedStatus, userId, isAnalyst, isReporter, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out var userId))
            return Results.Challenge();

        var isAnalyst = IncidentAccess.IsAnalyst(httpContext.User);
        var isReporter = IncidentAccess.IsReporter(httpContext.User);

        var incident = await queries.GetDetailsAsync(
            id, userId, isAnalyst, isReporter, cancellationToken);

        return incident is null
            ? IncidentNotFound()
            : Results.Ok(incident);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateIncidentRequest request,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out _))
            return Results.Challenge();

        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (incident is null || !IncidentAccess.CanMutateIncident(httpContext.User, incident.OwnerUserId))
            return IncidentNotFound();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Title))
            errors["title"] = ["Заголовок обов'язковий."];
        else if (request.Title.Length > 160)
            errors["title"] = ["Заголовок не довший за 160 символів."];

        if (string.IsNullOrWhiteSpace(request.Description))
            errors["description"] = ["Опис обов'язковий."];
        else if (request.Description.Length > 4000)
            errors["description"] = ["Опис не довший за 4000 символів."];

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";

        if ((incident.Severity == IncidentSeverity.High || incident.Severity == IncidentSeverity.Critical)
            && !errors.ContainsKey("description")
            && description.Length < 40)
        {
            errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        incident.Title = title;
        incident.Description = description;
        incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out _))
            return Results.Challenge();

        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (incident is null || !IncidentAccess.CanMutateIncident(httpContext.User, incident.OwnerUserId))
            return IncidentNotFound();

        if (incident.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Видалення неможливе",
                detail: "Видалити можна лише власний інцидент у початковому статусі New.",
                statusCode: StatusCodes.Status409Conflict);
        }

        dbContext.Incidents.Remove(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(
        Guid id,
        HttpContext httpContext,
        SecureLabDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out var subjectId))
            return Results.Challenge();

        var incident = await dbContext.Incidents
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (incident is null)
            return IncidentNotFound();

        if (!IncidentAccess.CanTriage(httpContext.User))
        {
            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
            logger.LogWarning(
                "authorization.denied subject={SubjectId} action={Action} resource={ResourceId} result={Result} occurredAtUtc={OccurredAtUtc:O} traceId={TraceId}",
                subjectId,
                "incident.triage",
                incident.Id,
                "denied",
                DateTimeOffset.UtcNow,
                traceId);

            return Results.Problem(
                title: "Forbidden",
                detail: "Тільки роль Analyst має право виконувати перехід статусу triage.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (incident.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Недопустимий перехід статусу",
                detail: "Перехід у статус Triaged дозволений лише зі стану New.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        incident.Status = IncidentStatus.Triaged;
        incident.UpdatedAtUtc = now;

        dbContext.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            OldStatus = IncidentStatus.New,
            NewStatus = IncidentStatus.Triaged,
            ChangedByUserId = subjectId,
            Note = "Статус оновлено на Triaged",
            CreatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCommentAsync(
        Guid id,
        HttpContext httpContext,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!IncidentAccess.TryGetUserId(httpContext.User, out var userId))
            return Results.Challenge();

        var isAnalyst = IncidentAccess.IsAnalyst(httpContext.User);
        var isReporter = IncidentAccess.IsReporter(httpContext.User);

        var comment = await queries.GetCommentAsync(
            id, userId, isAnalyst, isReporter, cancellationToken);

        return comment is null
            ? CommentNotFound()
            : Results.Ok(comment);
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await queries.GetSeveritySummaryAsync(cancellationToken));
    }

    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);

    private static IResult CommentNotFound() => Results.Problem(
        title: "Коментар не знайдено",
        statusCode: StatusCodes.Status404NotFound);
}