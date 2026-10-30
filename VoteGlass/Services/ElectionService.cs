using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record VoterCounts(int Approved, int Pending);

public class ElectionService(IDbContextFactory<ApplicationDbContext> factory, OrgContext org)
{
    // The real status, worked out from the clock
    public static ElectionStatus StatusOf(Election e)
    {
        if (e.Status is ElectionStatus.Draft or ElectionStatus.Closed) return e.Status;
        var now = DateTime.UtcNow;
        if (now < e.StartTime) return ElectionStatus.Scheduled;
        return now <= e.EndTime ? ElectionStatus.Live : ElectionStatus.Closed;
    }

    public static bool IsEditable(Election e) =>
        StatusOf(e) is ElectionStatus.Draft or ElectionStatus.Scheduled;

    public async Task<List<Election>> GetAllAsync()
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Elections.AsNoTracking().Include(e => e.Groups)
            .Where(e => e.OrganizationId == orgId)
            .OrderByDescending(e => e.StartTime).ToListAsync();
    }

    public async Task<Election?> GetAsync(int id)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Elections.AsNoTracking().Include(e => e.Groups)
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == orgId);
    }

    // For election links: any signed-in person may look up the basics. The pages they land on check eligibility.
    public async Task<Election?> GetAnyAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Elections.AsNoTracking().Include(e => e.Organization).FirstOrDefaultAsync(e => e.Id == id);
    }

    // Registered = people on this organisation's roster who have joined. Pending = still to join.
    public async Task<VoterCounts> GetVoterCountsAsync()
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var registered = await db.RosterEntries.CountAsync(r => r.OrganizationId == orgId && r.ClaimedByVoterProfileId != null);
        var waiting = await db.RosterEntries.CountAsync(r => r.OrganizationId == orgId && r.ClaimedByVoterProfileId == null);
        return new(registered, waiting);
    }

    public async Task<(bool Success, string? Error)> SaveAsync(string adminId, int? id, ElectionInput input)
    {
        var orgId = await org.RequireAsync();

        var start = AppTime.ToUtc(input.StartTime!.Value);
        var end = AppTime.ToUtc(input.EndTime!.Value);
        if (end <= start) return (false, "The end time must be after the start time.");
        if (end <= DateTime.UtcNow) return (false, "The end time must be in the future.");
        if (!input.OpenToEveryone && input.GroupIds.Count == 0)
            return (false, "Choose at least one group, or switch to \"Everyone\".");

        await using var db = await factory.CreateDbContextAsync();
        if (id is null && await db.Elections.CountAsync(e => e.OrganizationId == orgId) >= Limits.MaxElectionsPerOrganisation)
            return (false, $"An organisation can have up to {Limits.MaxElectionsPerOrganisation} elections. Delete old drafts to make room.");

        Election? election;
        if (id is null)
        {
            election = new Election { CreatedByUserId = adminId, Status = ElectionStatus.Draft, OrganizationId = orgId };
            db.Elections.Add(election);
        }
        else
        {
            election = await db.Elections.Include(e => e.Groups)
                .FirstOrDefaultAsync(e => e.Id == id.Value && e.OrganizationId == orgId);
            if (election is null) return (false, "Election not found.");
            if (!IsEditable(election)) return (false, "An election that has started can't be edited.");
        }

        election.Title = input.Title.Trim();
        election.Description = input.Description?.Trim();
        election.StartTime = start;
        election.EndTime = end;
        election.MinimumAge = input.MinimumAge;
        election.ResultVisibility = input.ResultVisibility;
        election.AccentColor = input.AccentColor;
        election.OpenToEveryone = input.OpenToEveryone;

        election.Groups.Clear();
        if (!input.OpenToEveryone)
        {
            var ids = input.GroupIds;
            var groups = await db.VoterGroups.Where(g => ids.Contains(g.Id) && g.OrganizationId == orgId).ToListAsync();
            foreach (var g in groups) election.Groups.Add(g);
        }
        await db.SaveChangesAsync();

        Log(db, adminId, id is null ? "CreateElection" : "UpdateElection", election);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<string?> PublishAsync(string adminId, int id)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Elections
            .Include(x => x.Positions).ThenInclude(p => p.Candidates)
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);

        if (e is null) return "Election not found.";
        if (e.Status != ElectionStatus.Draft) return "Only draft elections can be published.";
        if (e.EndTime <= DateTime.UtcNow) return "The end time has already passed. Edit the election first.";

        if (e.Positions.Count == 0)
            return "Add at least one position to the ballot before publishing.";

        foreach (var p in e.Positions)
        {
            var count = p.Candidates.Count;
            if (!(count >= 2 || (count == 1 && p.AllowAbstain)))
                return $"\"{p.Title}\" needs at least 2 candidates (or 1 candidate if abstaining is allowed).";
            if (p.MaxSelections > Math.Max(count, 1))
                return $"\"{p.Title}\" lets voters pick {p.MaxSelections}, but it only has {count} candidate(s).";
        }

        e.Status = ElectionStatus.Scheduled;
        Log(db, adminId, "PublishElection", e);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> UnpublishAsync(string adminId, int id)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Elections.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (e is null) return "Election not found.";
        if (StatusOf(e) != ElectionStatus.Scheduled) return "Only elections that haven't started can be unpublished.";

        e.Status = ElectionStatus.Draft;
        Log(db, adminId, "UnpublishElection", e);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DeleteAsync(string adminId, int id)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Elections.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (e is null) return "Election not found.";
        if (e.Status != ElectionStatus.Draft) return "Only draft elections can be deleted.";

        Log(db, adminId, "DeleteElection", e);
        db.Elections.Remove(e);
        await db.SaveChangesAsync();
        return null;
    }

    private static void Log(ApplicationDbContext db, string adminId, string action, Election e) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = e.OrganizationId,
            Action = action,
            EntityType = "Election",
            EntityId = e.Id.ToString(),
            Details = e.Title
        });
}