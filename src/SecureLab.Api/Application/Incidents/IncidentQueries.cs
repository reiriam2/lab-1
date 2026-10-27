using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(
    SecureLabDbContext dbContext,
    ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        Guid userId,
        bool isAnalyst,
        bool isReporter,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        if (!isAnalyst && !isReporter)
            return [];

        var query = dbContext.Incidents.AsNoTracking();

        if (!isAnalyst)
        {
            query = query.Where(incident => incident.OwnerUserId == userId);
        }

        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        Guid userId,
        bool isAnalyst,
        bool isReporter,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        if (!isAnalyst && !isReporter)
            return Task.FromResult<IncidentDetailsResponse?>(null);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id
                && (isAnalyst || (isReporter && incident.OwnerUserId == userId)))
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => isAnalyst || !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<IncidentCommentResponse?> GetCommentAsync(
        Guid commentId,
        Guid userId,
        bool isAnalyst,
        bool isReporter,
        CancellationToken cancellationToken)
    {
        if (!isAnalyst && !isReporter)
            return Task.FromResult<IncidentCommentResponse?>(null);

        return dbContext.IncidentComments
            .AsNoTracking()
            .Where(comment => comment.Id == commentId
                && (isAnalyst || (isReporter && comment.Incident.OwnerUserId == userId))
                && (isAnalyst || !comment.IsInternal))
            .Select(comment => new IncidentCommentResponse(
                comment.Id,
                comment.Author.DisplayName,
                comment.Text,
                comment.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        CancellationToken cancellationToken)
    {
        var groups = await dbContext.Incidents
            .AsNoTracking()
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return groups
            .OrderBy(group => group.Severity)
            .Select(group => new IncidentSeveritySummaryResponse(
                group.Severity.ToString(),
                group.Count))
            .ToList();
    }
}