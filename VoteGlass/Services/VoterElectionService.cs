using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record VoterElection(Election Election, ElectionStatus Status, string OrganizationName);
public record VoterMembership(int OrganizationId, string OrganizationName, string? GroupName);

public class VoterElectionService(IDbContextFactory<ApplicationDbContext> factory)
{
    // The one eligibility rule: a joined member of the election's organisation, old enough, in an allowed group
    public static bool IsEligible(Election e, VoterProfile voter, RosterEntry entry)
    {
        if (entry.Blocked || entry.ClaimedByVoterProfileId != voter.Id) return false;
        if (entry.OrganizationId != e.OrganizationId) return false;

        if (e.MinimumAge > 0)
        {
            var dob = entry.DateOfBirth ?? voter.DateOfBirth;       // the organiser's record wins
            var electionDay = DateOnly.FromDateTime(AppTime.ToLocal(e.StartTime));
            var age = electionDay.Year - dob.Year;
            if (dob > electionDay.AddYears(-age)) age--;
            if (age < e.MinimumAge) return false;
        }

        if (e.OpenToEveryone) return true;

        return entry.GroupName is not null &&
               e.Groups.Any(g => string.Equals(g.Name, entry.GroupName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<VoterMembership>> GetMembershipsAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var voterId = await db.VoterProfiles.Where(v => v.UserId == userId)
            .Select(v => (int?)v.Id).FirstOrDefaultAsync();
        if (voterId is null) return new();

        var rows = await db.RosterEntries.AsNoTracking()
            .Where(r => r.ClaimedByVoterProfileId == voterId && !r.Blocked)
            .Join(db.Organizations.Where(o => !o.IsSuspended), r => r.OrganizationId, o => o.Id,
                (r, o) => new { r.OrganizationId, o.Name, r.GroupName })
            .ToListAsync();

        return rows.Select(r => new VoterMembership(r.OrganizationId, r.Name, r.GroupName))
            .OrderBy(r => r.OrganizationName).ToList();
    }

    public async Task<bool> IsMemberAsync(string userId, int organizationId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var voterId = await db.VoterProfiles.Where(v => v.UserId == userId)
            .Select(v => (int?)v.Id).FirstOrDefaultAsync();
        return voterId is not null && await db.RosterEntries.AnyAsync(r =>
            r.OrganizationId == organizationId && r.ClaimedByVoterProfileId == voterId && !r.Blocked);
    }

    public async Task<List<VoterElection>> GetForVoterAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var voter = await db.VoterProfiles.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        if (voter is null) return new();

        var entries = await db.RosterEntries.AsNoTracking()
            .Where(r => r.ClaimedByVoterProfileId == voter.Id && !r.Blocked)
            .ToListAsync();
        var entryByOrg = entries.ToDictionary(r => r.OrganizationId);
        var orgIds = entryByOrg.Keys.ToList();

        var elections = await db.Elections.AsNoTracking()
            .Include(e => e.Groups)
            .Include(e => e.Organization)
            .Where(e => e.Status != ElectionStatus.Draft && orgIds.Contains(e.OrganizationId) && !e.Organization!.IsSuspended)
            .OrderBy(e => e.StartTime)
            .ToListAsync();

        return elections
            .Where(e => IsEligible(e, voter, entryByOrg[e.OrganizationId]))
            .Select(e => new VoterElection(e, ElectionService.StatusOf(e), e.Organization!.Name))
            .Where(x => x.Status != ElectionStatus.Closed || x.Election.EndTime > DateTime.UtcNow.AddDays(-30))
            .OrderBy(x => x.Status == ElectionStatus.Live ? 0 : x.Status == ElectionStatus.Scheduled ? 1 : 2)
            .ThenBy(x => x.Election.StartTime)
            .ToList();
    }
}