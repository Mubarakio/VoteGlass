using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public class BallotService(IDbContextFactory<ApplicationDbContext> factory, OrgContext org)
{
    public async Task<List<Position>> GetPositionsAsync(int electionId)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();

        if (!await db.Elections.AnyAsync(e => e.Id == electionId && e.OrganizationId == orgId))
            return new();

        return await db.Positions.AsNoTracking()
            .Where(p => p.ElectionId == electionId)
            .Include(p => p.Candidates.OrderBy(c => c.DisplayOrder))
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();
    }

    public async Task<string?> SavePositionAsync(string adminId, int electionId, int? positionId, PositionInput input)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var error = await CheckEditableAsync(db, electionId, orgId);
        if (error is not null) return error;

        Position? p;
        if (positionId is null)
        {
            var maxOrder = await db.Positions.Where(x => x.ElectionId == electionId)
                .MaxAsync(x => (int?)x.DisplayOrder) ?? 0;
            p = new Position { ElectionId = electionId, DisplayOrder = maxOrder + 1 };
            db.Positions.Add(p);
        }
        else
        {
            p = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId && x.ElectionId == electionId);
            if (p is null) return "Position not found.";
        }

        p.Title = input.Title.Trim();
        p.Description = input.Description?.Trim();
        p.MaxSelections = input.MaxSelections;
        p.AllowAbstain = input.AllowAbstain;
        p.TieHandling = input.TieHandling;
        await db.SaveChangesAsync();

        Log(db, adminId, orgId, positionId is null ? "AddPosition" : "UpdatePosition", "Position", p.Id, p.Title);
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DeletePositionAsync(string adminId, int positionId)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var p = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId);
        if (p is null) return "Position not found.";

        var error = await CheckEditableAsync(db, p.ElectionId, orgId);
        if (error is not null) return error;

        Log(db, adminId, orgId, "DeletePosition", "Position", p.Id, p.Title);
        db.Positions.Remove(p);   // its candidates are removed with it
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> SaveCandidateAsync(string adminId, int positionId, int? candidateId, CandidateInput input)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var position = await db.Positions.FirstOrDefaultAsync(x => x.Id == positionId);
        if (position is null) return "Position not found.";

        var error = await CheckEditableAsync(db, position.ElectionId, orgId);
        if (error is not null) return error;

        var name = input.FullName.Trim();
        var duplicate = await db.Candidates.AnyAsync(c =>
            c.PositionId == positionId && c.FullName == name && c.Id != candidateId);
        if (duplicate) return "A candidate with that name is already listed for this position.";

        Candidate? c;
        if (candidateId is null)
        {
            var maxOrder = await db.Candidates.Where(x => x.PositionId == positionId)
                .MaxAsync(x => (int?)x.DisplayOrder) ?? 0;
            c = new Candidate { PositionId = positionId, DisplayOrder = maxOrder + 1 };
            db.Candidates.Add(c);
        }
        else
        {
            c = await db.Candidates.FirstOrDefaultAsync(x => x.Id == candidateId && x.PositionId == positionId);
            if (c is null) return "Candidate not found.";
        }

        c.FullName = name;
        c.Manifesto = input.Manifesto?.Trim();
        await db.SaveChangesAsync();

        Log(db, adminId, orgId, candidateId is null ? "AddCandidate" : "UpdateCandidate", "Candidate", c.Id, $"{c.FullName} ({position.Title})");
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DeleteCandidateAsync(string adminId, int candidateId)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Candidates.Include(x => x.Position).FirstOrDefaultAsync(x => x.Id == candidateId);
        if (c is null) return "Candidate not found.";

        var error = await CheckEditableAsync(db, c.Position!.ElectionId, orgId);
        if (error is not null) return error;

        Log(db, adminId, orgId, "DeleteCandidate", "Candidate", c.Id, $"{c.FullName} ({c.Position.Title})");
        db.Candidates.Remove(c);
        await db.SaveChangesAsync();
        return null;
    }

    private static async Task<string?> CheckEditableAsync(ApplicationDbContext db, int electionId, int orgId)
    {
        var election = await db.Elections.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == electionId && e.OrganizationId == orgId);
        if (election is null) return "Election not found.";
        if (!ElectionService.IsEditable(election)) return "This election has started, so the ballot can't be changed.";
        return null;
    }

    private static void Log(ApplicationDbContext db, string adminId, int orgId, string action, string entityType, int entityId, string details) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = orgId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Details = details
        });
}