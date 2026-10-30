using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record VerificationResult(bool Success, string Message, string? MaskedEmail = null);
public record JoinInfo(int OrganizationId, string? OrganizationName, bool Available, bool HasProfile, bool IsMember);

public class VerificationService(IDbContextFactory<ApplicationDbContext> factory, IAppEmailSender email)
{
    public const int CodeMinutes = 10;
    public const int MaxAttempts = 5;
    public const int ResendSeconds = 60;

    public async Task<JoinInfo> GetJoinInfoAsync(string userId, string? code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync();

        var org = code.Length is >= 6 and <= 20
            ? await db.Organizations.AsNoTracking().Where(o => o.JoinCode == code)
                .Select(o => new { o.Id, o.Name, o.IsSuspended }).FirstOrDefaultAsync()
            : null;
        if (org is null) return new(0, null, false, false, false);

        var profileId = await db.VoterProfiles.Where(v => v.UserId == userId)
            .Select(v => (int?)v.Id).FirstOrDefaultAsync();

        var member = profileId is not null && await db.RosterEntries.AnyAsync(r =>
            r.OrganizationId == org.Id && r.ClaimedByVoterProfileId == profileId && !r.Blocked);

        return new(org.Id, org.Name, !org.IsSuspended, profileId is not null, member);
    }

    public async Task<VerificationResult> RequestCodeAsync(string userId, int organizationId, string? registrationNumber)
    {
        var regNo = (registrationNumber ?? "").Trim().ToUpperInvariant();
        if (regNo.Length == 0) return Fail("Enter your registration number.");

        await using var db = await factory.CreateDbContextAsync();

        var profile = await db.VoterProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
        if (profile is null) return Fail("Please save your voter profile first.");

        var organisation = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId);
        if (organisation is null || organisation.IsSuspended) return Fail("This organisation isn't available.");

        if (await db.RosterEntries.AnyAsync(r => r.OrganizationId == organizationId && r.ClaimedByVoterProfileId == profile.Id))
            return Fail("You've already joined this organisation.");

        var entry = await db.RosterEntries.FirstOrDefaultAsync(r =>
            r.OrganizationId == organizationId && r.RegistrationNumber == regNo);
        if (entry is null)
            return Fail("That registration number isn't on this organisation's voter list. Check it, or ask the organiser.");
        if (entry.Blocked)
            return Fail("Your access to this organisation was removed. Please contact the organiser.");
        if (string.IsNullOrWhiteSpace(entry.Email))
            return Fail("There's no email address on file for you. Please contact the organiser.");
        if (entry.ClaimedByVoterProfileId is not null)
            return Fail("This registration number has already been used by another account. Please contact the organiser.");

        // Codes this person asked for in this organisation
        var orgEntryIds = db.RosterEntries.Where(r => r.OrganizationId == organizationId).Select(r => r.Id);
        var existing = await db.VerificationCodes
            .Where(c => c.VoterProfileId == profile.Id && orgEntryIds.Contains(c.RosterEntryId))
            .ToListAsync();

        var last = existing.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
        if (last is not null)
        {
            var wait = ResendSeconds - (int)(DateTime.UtcNow - last.CreatedAt).TotalSeconds;
            if (wait > 0) return Fail($"Please wait {wait} seconds before asking for another code.");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        db.VerificationCodes.RemoveRange(existing);          // only the newest code is valid
        db.VerificationCodes.Add(new VerificationCode
        {
            VoterProfileId = profile.Id,
            RosterEntryId = entry.Id,
            Salt = salt,
            CodeHash = Hash(salt, code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(CodeMinutes)
        });
        await db.SaveChangesAsync();

        await email.SendAsync(entry.Email, "Your VoteGlass verification code",
            $"Hello {entry.FullName},\n\nYour VoteGlass verification code is: {code}\n\n" +
            $"Use it to join {organisation.Name}. It is valid for {CodeMinutes} minutes. " +
            "If you didn't ask for it, you can ignore this email.");

        return new(true, "Code sent.", Mask(entry.Email));
    }

    public async Task<VerificationResult> VerifyCodeAsync(string userId, int organizationId, string? input)
    {
        var code = new string((input ?? "").Where(char.IsDigit).ToArray());
        if (code.Length != 6) return Fail("Enter the 6-digit code from the email.");

        await using var db = await factory.CreateDbContextAsync();

        var profile = await db.VoterProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
        if (profile is null) return Fail("Please save your voter profile first.");

        var orgEntryIds = db.RosterEntries.Where(r => r.OrganizationId == organizationId).Select(r => r.Id);
        var saved = await db.VerificationCodes
            .Where(c => c.VoterProfileId == profile.Id && c.ConsumedAt == null && orgEntryIds.Contains(c.RosterEntryId))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (saved is null || saved.ExpiresAt < DateTime.UtcNow)
            return Fail("That code has expired. Please ask for a new one.");
        if (saved.Attempts >= MaxAttempts)
            return Fail("Too many wrong attempts. Please ask for a new code.");

        var expected = Convert.FromBase64String(saved.CodeHash);
        var actual = Convert.FromBase64String(Hash(saved.Salt, code));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            saved.Attempts++;
            await db.SaveChangesAsync();
            var left = MaxAttempts - saved.Attempts;
            return Fail(left > 0
                ? $"That code isn't correct. {left} attempt(s) left."
                : "Too many wrong attempts. Please ask for a new code.");
        }

        var entry = await db.RosterEntries.FirstOrDefaultAsync(r =>
            r.Id == saved.RosterEntryId && r.OrganizationId == organizationId);
        if (entry is null || entry.Blocked)
            return Fail("You can't join this organisation right now. Please contact the organiser.");
        if (entry.ClaimedByVoterProfileId is not null && entry.ClaimedByVoterProfileId != profile.Id)
            return Fail("This registration number has already been used by another account. Please contact the organiser.");

        saved.ConsumedAt = DateTime.UtcNow;
        entry.ClaimedByVoterProfileId = profile.Id;
        entry.ClaimedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = organizationId,
            Action = "MemberJoined",
            EntityType = "Roster",
            EntityId = entry.Id.ToString(),
            Details = $"{entry.FullName} ({entry.RegistrationNumber})"
        });

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Fail("You've already joined this organisation."); }

        return new(true, "Joined");
    }

    private static VerificationResult Fail(string message) => new(false, message);

    private static string Hash(string salt, string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(salt + code)));

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "your email";
        return email[0] + new string('*', Math.Min(at - 1, 5)) + email[at..];
    }
}