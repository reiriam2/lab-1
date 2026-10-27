using System.Security.Claims;
using SecureLab.Api.Data;

namespace SecureLab.Api.Application.Incidents;

public static class IncidentAccess
{
    public static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    public static bool IsReporter(ClaimsPrincipal user) =>
        user.IsInRole(DbSeeder.ReporterRole);

    public static bool IsAnalyst(ClaimsPrincipal user) =>
        user.IsInRole(DbSeeder.AnalystRole);

    public static bool CanReadIncident(ClaimsPrincipal user, Guid ownerUserId)
    {
        if (!TryGetUserId(user, out var userId))
            return false;

        return IsAnalyst(user) || (IsReporter(user) && ownerUserId == userId);
    }

    public static bool CanMutateIncident(ClaimsPrincipal user, Guid ownerUserId)
    {
        if (!TryGetUserId(user, out var userId))
            return false;

        return IsReporter(user) && ownerUserId == userId;
    }

    public static bool CanTriage(ClaimsPrincipal user) =>
        TryGetUserId(user, out _) && IsAnalyst(user);

    public static bool CanReadComment(ClaimsPrincipal user, Guid incidentOwnerUserId, bool isInternal)
    {
        if (!CanReadIncident(user, incidentOwnerUserId))
            return false;

        return !isInternal || IsAnalyst(user);
    }
}