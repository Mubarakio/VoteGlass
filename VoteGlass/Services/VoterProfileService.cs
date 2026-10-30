using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public class VoterProfileService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<VoterProfile?> GetByUserIdAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.VoterProfiles.AsNoTracking().FirstOrDefaultAsync(v => v.UserId == userId);
    }

    // Once someone has joined an organisation, their date of birth is locked
    public async Task<bool> HasJoinedAnyAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var profileId = await db.VoterProfiles.Where(v => v.UserId == userId)
            .Select(v => (int?)v.Id).FirstOrDefaultAsync();
        return profileId is not null &&
               await db.RosterEntries.AnyAsync(r => r.ClaimedByVoterProfileId == profileId);
    }

    public async Task<(bool Success, string? Error)> SaveAsync(string userId, VoterProfileInput input)
    {
        var dob = input.DateOfBirth!.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dob > today) return (false, "Date of birth can't be in the future.");

        var phone = NormalizePhone(input.PhoneNumber);

        await using var db = await factory.CreateDbContextAsync();

        if (await db.VoterProfiles.AnyAsync(v => v.PhoneNumber == phone && v.UserId != userId))
            return (false, "That phone number is already registered to another account.");

        var profile = await db.VoterProfiles.FirstOrDefaultAsync(v => v.UserId == userId);
        if (profile is null)
        {
            profile = new VoterProfile { UserId = userId };
            db.VoterProfiles.Add(profile);
        }

        var joined = profile.Id != 0 &&
                     await db.RosterEntries.AnyAsync(r => r.ClaimedByVoterProfileId == profile.Id);

        profile.FullName = input.FullName.Trim();
        profile.PhoneNumber = phone;
        profile.Gender = input.Gender;
        profile.Address = input.Address.Trim();
        if (!joined) profile.DateOfBirth = dob;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            return (false, "Could not save your profile. Check that your phone number isn't already in use.");
        }

        return (true, null);
    }

    private static string NormalizePhone(string phone) =>
        new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
}