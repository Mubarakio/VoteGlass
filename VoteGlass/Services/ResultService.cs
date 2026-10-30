using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record CandidateResult(int Id, string Name, int Votes, double Percent, bool IsWinner, bool IsTied);

public record PositionResult(
    int PositionId, string Title, int Seats, bool AllowAbstain, TieHandling TieHandling,
    List<CandidateResult> Candidates, int Abstain, int Total,
    int TiedSeats, List<int> TiedIds, bool TieSettled);

public record ElectionResults(
    Election Election, ElectionStatus Status, int Voted, int Eligible,
    bool CountsVisible, string? HiddenMessage, List<PositionResult> Positions);

public class ResultsService(IDbContextFactory<ApplicationDbContext> factory, OrgContext org)
{
    // What voters may see
    public static bool VotersCanSee(Election e) => ElectionService.StatusOf(e) switch
    {
        ElectionStatus.Live => e.ResultVisibility == ResultVisibility.Live,
        ElectionStatus.Closed => e.ResultVisibility != ResultVisibility.AfterPublish || e.ResultsPublished,
        _ => false
    };

    // What the admin may see: live counts only if the election allows live results, everything once closed
    public static bool AdminCanSee(Election e) => ElectionService.StatusOf(e) switch
    {
        ElectionStatus.Live => e.ResultVisibility == ResultVisibility.Live,
        ElectionStatus.Closed => true,
        _ => false
    };

    public async Task<ElectionResults?> GetAsync(int electionId, bool forAdmin)
    {
        await using var db = await factory.CreateDbContextAsync();

        var election = await db.Elections.AsNoTracking().Include(e => e.Groups)
        .FirstOrDefaultAsync(e => e.Id == electionId);
        if (election is null || election.Status == ElectionStatus.Draft) return null;

        if (forAdmin && election.OrganizationId != await org.RequireAsync()) return null;
        var status = ElectionService.StatusOf(election);
        var voted = await db.VoteRecords.Where(v => v.ElectionId == electionId)
            .Select(v => v.VoterProfileId).Distinct().CountAsync();
        var eligible = await CountEligibleAsync(db, election);

        var visible = forAdmin ? AdminCanSee(election) : VotersCanSee(election);
        if (!visible)
        {
            var message = status switch
            {
                ElectionStatus.Scheduled => "Voting hasn't started yet.",
                ElectionStatus.Live => "The results are hidden until the election closes.",
                _ => "The results haven't been published yet."
            };
            return new(election, status, voted, eligible, false, message, new());
        }

        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.ElectionId == electionId)
            .Include(p => p.Candidates.OrderBy(c => c.DisplayOrder))
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();

        var counts = await db.Ballots.Where(b => b.ElectionId == electionId)
            .GroupBy(b => new { b.PositionId, b.CandidateId })
            .Select(g => new { g.Key.PositionId, g.Key.CandidateId, N = g.Count() })
            .ToListAsync();

        var results = positions.Select(p =>
        {
            var rows = counts.Where(c => c.PositionId == p.Id).ToList();
            var votes = rows.Where(r => r.CandidateId != null).ToDictionary(r => r.CandidateId!.Value, r => r.N);
            var abstain = rows.Where(r => r.CandidateId == null).Sum(r => r.N);
            return Compute(p, votes, abstain, p.TieBreakWinners);
        }).ToList();

        return new(election, status, voted, eligible, true, null, results);
    }

    public async Task<ElectionResults?> GetForVoterAsync(string userId, int electionId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var election = await db.Elections.AsNoTracking().Include(e => e.Groups)
            .FirstOrDefaultAsync(e => e.Id == electionId);
        if (election is null || election.Status == ElectionStatus.Draft) return null;
        if (await db.Organizations.AnyAsync(o => o.Id == election.OrganizationId && o.IsSuspended)) return null;

        var voter = await db.VoterProfiles.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        if (voter is null) return null;

        var entry = await db.RosterEntries.AsNoTracking().FirstOrDefaultAsync(r =>
            r.OrganizationId == election.OrganizationId && r.ClaimedByVoterProfileId == voter.Id && !r.Blocked);
        if (entry is null || !VoterElectionService.IsEligible(election, voter, entry)) return null;

        return await GetAsync(electionId, forAdmin: false);
    }
    public async Task<string?> SetPublishedAsync(string adminId, int electionId, bool published)
    {
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.Elections.FindAsync(electionId);
        if (e is null || e.OrganizationId != await org.RequireAsync()) return "Election not found.";
        if (ElectionService.StatusOf(e) != ElectionStatus.Closed)
            return "Results can only be published after the election has closed.";

        e.ResultsPublished = published;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = published ? "PublishResults" : "HideResults",
            EntityType = "Election",
            EntityId = e.Id.ToString(),
            Details = e.Title
        });
        await db.SaveChangesAsync();
        return null;
    }

    // An empty list clears an earlier decision
    public async Task<string?> SaveTieDecisionAsync(string adminId, int positionId, List<int> winnerIds)
    {
        await using var db = await factory.CreateDbContextAsync();

        var p = await db.Positions.Include(x => x.Candidates).FirstOrDefaultAsync(x => x.Id == positionId);
        if (p is null) return "Position not found.";

        var e = await db.Elections.FindAsync(p.ElectionId);
        if (e is not null && e.OrganizationId != await org.RequireAsync()) return "Position not found.";
        if (e is null || ElectionService.StatusOf(e) != ElectionStatus.Closed)
            return "A tie can only be settled after the election has closed.";
        if (p.TieHandling != TieHandling.AdminDecides)
            return "This position isn't set to be decided by the admin.";

        var rows = await db.Ballots.Where(b => b.PositionId == positionId)
            .GroupBy(b => b.CandidateId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var votes = rows.Where(r => r.Key != null).ToDictionary(r => r.Key!.Value, r => r.N);
        var abstain = rows.Where(r => r.Key == null).Sum(r => r.N);
        var calc = Compute(p, votes, abstain, null);

        if (winnerIds.Count == 0)
        {
            p.TieBreakWinners = null;
        }
        else
        {
            if (calc.TiedSeats == 0) return "There is no tie to settle for this position.";
            var chosen = winnerIds.Distinct().ToList();
            if (chosen.Count != calc.TiedSeats || chosen.Any(id => !calc.TiedIds.Contains(id)))
                return $"Choose exactly {calc.TiedSeats} of the tied candidates.";
            p.TieBreakWinners = string.Join(",", chosen);
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = winnerIds.Count == 0 ? "ClearTieDecision" : "SettleTie",
            EntityType = "Position",
            EntityId = p.Id.ToString(),
            Details = p.Title
        });
        await db.SaveChangesAsync();
        return null;
    }

    private static async Task<int> CountEligibleAsync(ApplicationDbContext db, Election e)
    {
        var members = await (
            from r in db.RosterEntries.AsNoTracking()
            where r.OrganizationId == e.OrganizationId && r.ClaimedByVoterProfileId != null && !r.Blocked
            join v in db.VoterProfiles.AsNoTracking() on r.ClaimedByVoterProfileId equals (int?)v.Id
            select new { Entry = r, Voter = v }).ToListAsync();

        return members.Count(m => VoterElectionService.IsEligible(e, m.Voter, m.Entry));
    }

    // Works out the winners and any tie for one position
    private static PositionResult Compute(Position p, Dictionary<int, int> votes, int abstain, string? tieBreak)
    {
        int V(int id) => votes.TryGetValue(id, out var n) ? n : 0;

        var total = p.Candidates.Sum(c => V(c.Id)) + abstain;
        var seats = Math.Max(1, p.MaxSelections);

        var ranked = p.Candidates
            .Select(c => (Cand: c, Votes: V(c.Id)))
            .OrderByDescending(x => x.Votes).ThenBy(x => x.Cand.DisplayOrder)
            .ToList();
        var positive = ranked.Where(x => x.Votes > 0).ToList();

        var winners = new HashSet<int>();
        var tied = new List<int>();
        var tiedSeats = 0;
        var settled = false;

        if (positive.Count <= seats)
        {
            winners.UnionWith(positive.Select(x => x.Cand.Id));
        }
        else
        {
            var cutoff = positive[seats - 1].Votes;
            var above = positive.Where(x => x.Votes > cutoff).Select(x => x.Cand.Id).ToList();
            var atCutoff = positive.Where(x => x.Votes == cutoff).Select(x => x.Cand.Id).ToList();

            winners.UnionWith(above);
            if (above.Count + atCutoff.Count <= seats)
            {
                winners.UnionWith(atCutoff);
            }
            else
            {
                tied = atCutoff;                       // more candidates share the last places than there are places
                tiedSeats = seats - above.Count;
            }
        }

        if (tiedSeats > 0 && !string.IsNullOrWhiteSpace(tieBreak))
        {
            var chosen = tieBreak.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? id : -1)
                .Where(id => tied.Contains(id)).Distinct().ToList();
            if (chosen.Count == tiedSeats)
            {
                winners.UnionWith(chosen);
                settled = true;
            }
        }

        var candidates = ranked.Select(x => new CandidateResult(
            x.Cand.Id, x.Cand.FullName, x.Votes,
            total == 0 ? 0 : x.Votes * 100.0 / total,
            winners.Contains(x.Cand.Id),
            tied.Contains(x.Cand.Id) && !settled)).ToList();

        return new(p.Id, p.Title, seats, p.AllowAbstain, p.TieHandling, candidates, abstain, total, tiedSeats, tied, settled);
    }
}