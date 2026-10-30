using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record PositionChoice(bool Abstain, List<int> CandidateIds);
public record BallotView(Election Election, List<Position> Positions, bool AlreadyVoted, string? Problem, bool NeedsJoin = false);
public record VoteResult(bool Success, string? Error);

public class VotingService(IDbContextFactory<ApplicationDbContext> factory, ResultsNotifier notifier)
{
    // Everything the vote page needs. Problem is set when the voter can't vote right now.
    public async Task<BallotView?> GetBallotAsync(string userId, int electionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var election = await db.Elections.AsNoTracking().Include(e => e.Groups).Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == electionId);
        if (election is null || election.Status == ElectionStatus.Draft) return null;

        var (voter, entry) = await LoadMemberAsync(db, userId, election.OrganizationId);
        if (voter is null)
            return new(election, new(), false, "Please complete your voter profile first.");
        if (entry is null)
            return new(election, new(), false,
                "You haven't joined this organisation yet. Join it with the registration number on its voter list.", true);
        if (await db.Organizations.AnyAsync(o => o.Id == election.OrganizationId && o.IsSuspended))
            return new(election, new(), false, "This organisation is currently unavailable.");
        if (!VoterElectionService.IsEligible(election, voter, entry))
            return new(election, new(), false, "You aren't eligible to vote in this election.");

        var voted = await db.VoteRecords.AnyAsync(v => v.ElectionId == electionId && v.VoterProfileId == voter.Id);

        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.ElectionId == electionId)
            .Include(p => p.Candidates.OrderBy(c => c.DisplayOrder))
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();

        string? problem = null;
        if (!voted)
        {
            problem = ElectionService.StatusOf(election) switch
            {
                ElectionStatus.Scheduled => $"Voting opens on {AppTime.Format(election.StartTime)}.",
                ElectionStatus.Closed => "Voting has closed for this election.",
                _ => positions.Count == 0 ? "This election doesn't have a ballot yet." : null
            };
        }

        return new(election, positions, voted, problem);
    }

    public async Task<VoteResult> CastVoteAsync(string userId, int electionId, Dictionary<int, PositionChoice> choices)
    {
        await using var db = await factory.CreateDbContextAsync();

        var election = await db.Elections.Include(e => e.Groups).FirstOrDefaultAsync(e => e.Id == electionId);
        if (election is null) return Fail("Election not found.");
        if (ElectionService.StatusOf(election) != ElectionStatus.Live) return Fail("Voting isn't open for this election.");
        if (await db.Organizations.AnyAsync(o => o.Id == election.OrganizationId && o.IsSuspended))
            return Fail("This organisation is currently unavailable.");

        var (voter, entry) = await LoadMemberAsync(db, userId, election.OrganizationId);
        if (voter is null || entry is null || !VoterElectionService.IsEligible(election, voter, entry))
            return Fail("You aren't eligible to vote in this election.");

        if (await db.VoteRecords.AnyAsync(v => v.ElectionId == electionId && v.VoterProfileId == voter.Id))
            return Fail("You've already voted in this election.");

        var positions = await db.Positions.Include(p => p.Candidates)
            .Where(p => p.ElectionId == electionId).ToListAsync();
        if (positions.Count == 0) return Fail("This election doesn't have a ballot yet.");

        // Check every position before saving anything
        var ballots = new List<Ballot>();
        foreach (var p in positions)
        {
            if (!choices.TryGetValue(p.Id, out var choice))
                return Fail($"Please make a choice for \"{p.Title}\".");

            if (choice.Abstain)
            {
                if (!p.AllowAbstain) return Fail($"Abstaining isn't allowed for \"{p.Title}\".");
                if (choice.CandidateIds.Count > 0) return Fail($"Invalid choice for \"{p.Title}\".");
                ballots.Add(new Ballot { ElectionId = electionId, PositionId = p.Id, CandidateId = null });
                continue;
            }

            var ids = choice.CandidateIds.Distinct().ToList();
            if (ids.Count == 0) return Fail($"Please make a choice for \"{p.Title}\".");
            if (ids.Count > p.MaxSelections) return Fail($"You can choose at most {p.MaxSelections} for \"{p.Title}\".");
            if (ids.Any(id => p.Candidates.All(c => c.Id != id))) return Fail($"Invalid choice for \"{p.Title}\".");

            foreach (var id in ids)
                ballots.Add(new Ballot { ElectionId = electionId, PositionId = p.Id, CandidateId = id });
        }

        // The ballots carry no voter. The records say who voted, but never for whom.
        db.Ballots.AddRange(ballots);
        foreach (var p in positions)
            db.VoteRecords.Add(new VoteRecord { ElectionId = electionId, PositionId = p.Id, VoterProfileId = voter.Id });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = election.OrganizationId,
            Action = "VoteCast",
            EntityType = "Election",
            EntityId = electionId.ToString(),
            Details = election.Title          // never what was chosen
        });

        try
        {
            await db.SaveChangesAsync();      // one transaction: all of it, or none of it
        }
        catch (DbUpdateException)
        {
            return Fail("You've already voted in this election.");
        }

        notifier.Notify(electionId);
        return new(true, null);
    }

    public async Task<HashSet<int>> GetVotedElectionIdsAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var voterId = await db.VoterProfiles.Where(v => v.UserId == userId)
            .Select(v => (int?)v.Id).FirstOrDefaultAsync();
        if (voterId is null) return new();

        var ids = await db.VoteRecords.Where(v => v.VoterProfileId == voterId)
            .Select(v => v.ElectionId).Distinct().ToListAsync();
        return ids.ToHashSet();
    }

    // For the admin: how many people have voted in each election
    public async Task<Dictionary<int, int>> GetTurnoutAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.VoteRecords
            .GroupBy(v => v.ElectionId)
            .Select(g => new { ElectionId = g.Key, Voters = g.Select(x => x.VoterProfileId).Distinct().Count() })
            .ToDictionaryAsync(x => x.ElectionId, x => x.Voters);
    }

    // The voter's profile, and their joined (not blocked) roster entry in this organisation
    private static async Task<(VoterProfile? Voter, RosterEntry? Entry)> LoadMemberAsync(
        ApplicationDbContext db, string userId, int organizationId)
    {
        var voter = await db.VoterProfiles.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
        if (voter is null) return (null, null);

        var entry = await db.RosterEntries.AsNoTracking().FirstOrDefaultAsync(r =>
            r.OrganizationId == organizationId && r.ClaimedByVoterProfileId == voter.Id && !r.Blocked);
        return (voter, entry);
    }

    private static VoteResult Fail(string error) => new(false, error);
}