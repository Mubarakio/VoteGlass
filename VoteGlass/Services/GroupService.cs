using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record GroupInfo(int Id, string Name, int People);

public class GroupService(IDbContextFactory<ApplicationDbContext> factory, OrgContext org)
{
    // Turns the group names on this organisation's roster into selectable groups
    public async Task<List<GroupInfo>> GetGroupsAsync()
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();

        var rosterGroups = await db.RosterEntries
            .Where(r => r.OrganizationId == orgId && r.GroupName != null && r.GroupName != "")
            .GroupBy(r => r.GroupName!)
            .Select(g => new { Name = g.Key, People = g.Count() })
            .ToListAsync();

        var existing = await db.VoterGroups.Where(g => g.OrganizationId == orgId).ToListAsync();
        var known = existing.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var rg in rosterGroups)
        {
            if (known.Add(rg.Name))
            {
                db.VoterGroups.Add(new VoterGroup { OrganizationId = orgId, Name = rg.Name });
                added = true;
            }
        }

        if (added)
        {
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { /* another request created it first */ }
            existing = await db.VoterGroups.AsNoTracking().Where(g => g.OrganizationId == orgId).ToListAsync();
        }

        var counts = rosterGroups.ToDictionary(x => x.Name, x => x.People, StringComparer.OrdinalIgnoreCase);
        return existing
            .Where(g => counts.ContainsKey(g.Name))
            .Select(g => new GroupInfo(g.Id, g.Name, counts[g.Name]))
            .OrderBy(g => g.Name)
            .ToList();
    }
}