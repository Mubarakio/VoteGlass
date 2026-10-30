using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record PlatformStats(int Organisations, int Suspended, int Users, int Elections, int Ballots);
public record PlatformOrg(int Id, string Name, string? OwnerEmail, DateTime CreatedAt,
                          int Elections, int Roster, int Members, bool IsSuspended);
public record PlatformPageData(PlatformStats Stats, List<PlatformOrg> Items, int Total);

public class PlatformService(IDbContextFactory<ApplicationDbContext> factory, AuthenticationStateProvider auth, IConfiguration config)
{
    public const int PageSize = 20;

    // Only the platform owner (the SeedAdmin account) may use this service
    private async Task<string> RequireOwnerAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        var user = state.User;
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name;
        var main = config["SeedAdmin:Email"];

        if (!user.IsInRole("Admin") || string.IsNullOrWhiteSpace(main) ||
            !string.Equals(email, main, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException();

        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    }

    public async Task<PlatformPageData> GetAsync(string? search, int page)
    {
        await RequireOwnerAsync();
        await using var db = await factory.CreateDbContextAsync();

        var stats = new PlatformStats(
            await db.Organizations.CountAsync(),
            await db.Organizations.CountAsync(o => o.IsSuspended),
            await db.Users.CountAsync(),
            await db.Elections.CountAsync(),
            await db.VoteRecords.Select(v => new { v.ElectionId, v.VoterProfileId }).Distinct().CountAsync());

        var q = db.Organizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => o.Name.Contains(s));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.CreatedAt,
                o.IsSuspended,
                Elections = db.Elections.Count(e => e.OrganizationId == o.Id),
                Roster = db.RosterEntries.Count(r => r.OrganizationId == o.Id),
                Members = db.RosterEntries.Count(r => r.OrganizationId == o.Id && r.ClaimedByVoterProfileId != null)
            })
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var owners = await db.OrganizationMembers.AsNoTracking()
            .Where(m => ids.Contains(m.OrganizationId) && m.Role == OrgRole.Owner)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.OrganizationId, u.Email })
            .ToListAsync();
        var ownerByOrg = owners.GroupBy(o => o.OrganizationId).ToDictionary(g => g.Key, g => g.First().Email);

        var items = rows.Select(r => new PlatformOrg(
            r.Id, r.Name, ownerByOrg.GetValueOrDefault(r.Id), r.CreatedAt,
            r.Elections, r.Roster, r.Members, r.IsSuspended)).ToList();

        return new(stats, items, total);
    }

    public async Task<string?> SetSuspendedAsync(int organizationId, bool suspended)
    {
        var adminId = await RequireOwnerAsync();
        await using var db = await factory.CreateDbContextAsync();

        var org = await db.Organizations.FindAsync(organizationId);
        if (org is null) return "Organisation not found.";

        org.IsSuspended = suspended;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = organizationId,
            Action = suspended ? "SuspendOrganisation" : "ReactivateOrganisation",
            EntityType = "Organization",
            EntityId = organizationId.ToString(),
            Details = org.Name
        });
        await db.SaveChangesAsync();
        return null;
    }
}