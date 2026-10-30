using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record InviteResult(int Sent, int Remaining, string? Error);

public class InvitationService(IDbContextFactory<ApplicationDbContext> factory, IAppEmailSender email, OrgContext org)
{
    public const int BatchSize = 100;

    private static async Task<int> RemainingTodayAsync(ApplicationDbContext db, int organizationId)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var sent = await db.RosterEntries.CountAsync(r => r.OrganizationId == organizationId && r.InvitedAt > since);
        return Math.Max(0, Limits.MaxInvitesPerDay - sent);
    }

    public async Task<InviteResult> InviteOneAsync(string adminId, string baseUrl, int entryId)
    {
        var access = await org.GetAsync() ?? throw new UnauthorizedAccessException("Choose an organisation first.");
        if (access.JoinCode is null) return new(0, 0, "This organisation has no join link yet. Restart the app and try again.");

        await using var db = await factory.CreateDbContextAsync();

        var e = await db.RosterEntries.FirstOrDefaultAsync(x => x.Id == entryId && x.OrganizationId == access.OrganizationId);
        if (e is null) return new(0, 0, "Entry not found.");
        if (string.IsNullOrWhiteSpace(e.Email)) return new(0, 0, "This person has no email address.");
        if (e.ClaimedByVoterProfileId is not null) return new(0, 0, "This person has already registered.");

        if (await RemainingTodayAsync(db, access.OrganizationId) <= 0)
            return new(0, 0, $"You've reached today's limit of {Limits.MaxInvitesPerDay} invitations. Try again tomorrow.");

        await SendInviteAsync(db, e, access.OrganizationName, access.JoinCode, baseUrl);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = access.OrganizationId,
            Action = "InviteRosterEntry",
            EntityType = "Roster",
            EntityId = e.Id.ToString(),
            Details = $"{e.FullName} ({e.RegistrationNumber})"
        });
        await db.SaveChangesAsync();
        return new(1, 0, null);
    }

    public async Task<InviteResult> InviteAllNewAsync(string adminId, string baseUrl)
    {
        var access = await org.GetAsync() ?? throw new UnauthorizedAccessException("Choose an organisation first.");
        if (access.JoinCode is null) return new(0, 0, "This organisation has no join link yet. Restart the app and try again.");

        await using var db = await factory.CreateDbContextAsync();

        var query = db.RosterEntries.Where(x => x.OrganizationId == access.OrganizationId &&
            x.Email != null && x.Email != "" && x.ClaimedByVoterProfileId == null && x.InvitedAt == null);
        var total = await query.CountAsync();
        if (total == 0) return new(0, 0, null);

        var remaining = await RemainingTodayAsync(db, access.OrganizationId);
        if (remaining <= 0)
            return new(0, total, $"You've reached today's limit of {Limits.MaxInvitesPerDay} invitations. Try again tomorrow.");

        var batch = await query.OrderBy(x => x.Id).Take(Math.Min(BatchSize, remaining)).ToListAsync();
        foreach (var e in batch)
            await SendInviteAsync(db, e, access.OrganizationName, access.JoinCode, baseUrl);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = access.OrganizationId,
            Action = "InviteAllRoster",
            EntityType = "Roster",
            Details = $"{batch.Count} invitations sent"
        });
        await db.SaveChangesAsync();
        return new(batch.Count, total - batch.Count, null);
    }

    private async Task SendInviteAsync(ApplicationDbContext db, RosterEntry e, string organisationName, string joinCode, string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        await email.SendAsync(e.Email!, $"You're invited to vote with {organisationName}",
            $"Hello {e.FullName},\n\n" +
            $"You've been added to the voter list of {organisationName} on VoteGlass.\n\n" +
            "To get ready to vote:\n" +
            $"1. Open this link and sign in, or create an account: {url}/join/{joinCode}\n" +
            "2. Fill in your voter profile if you're asked to.\n" +
            $"3. Enter this registration number when you're asked: {e.RegistrationNumber}\n" +
            "4. We'll email a verification code to this address to confirm it's you.");

        e.InvitedAt = DateTime.UtcNow;
        e.InviteCount++;
        await db.SaveChangesAsync();
    }
}