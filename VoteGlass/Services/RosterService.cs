using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record RosterRow(int Line, string RegistrationNumber, string FullName, string? Email, string? Phone, DateOnly? DateOfBirth, string? Group);
public record RosterPreview(List<RosterRow> Rows, List<string> Errors, int NewCount, int UpdateCount, int NoContactCount);
public record RosterPage(List<RosterEntry> Items, int Total);

public class RosterService(IDbContextFactory<ApplicationDbContext> factory, OrgContext org)
{
    public const int PageSize = 25;

    public async Task<RosterPreview> PreviewAsync(string csv)
    {
        var orgId = await org.RequireAsync();
        var errors = new List<string>();
        var rows = new List<RosterRow>();
        var lines = ParseCsv(csv);

        if (lines.Count == 0)
        {
            errors.Add("The file is empty.");
            return new(rows, errors, 0, 0, 0);
        }

        var header = lines[0].Select(h => h.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "")).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));

        var iReg = Col("registrationnumber", "regno", "regnumber", "id", "idnumber", "matricnumber", "staffid");
        var iName = Col("fullname", "name");
        var iEmail = Col("email", "emailaddress");
        var iPhone = Col("phone", "phonenumber");
        var iDob = Col("dateofbirth", "dob");
        var iGroup = Col("group", "groupname", "department");

        if (iReg < 0 || iName < 0)
        {
            errors.Add("The first row must be a header that contains at least RegistrationNumber and FullName.");
            return new(rows, errors, 0, 0, 0);
        }

        string? Get(List<string> r, int i) =>
            i >= 0 && i < r.Count && !string.IsNullOrWhiteSpace(r[i]) ? r[i].Trim() : null;

        var seen = new HashSet<string>();
        for (int n = 1; n < lines.Count; n++)
        {
            var r = lines[n];
            var line = n + 1;
            if (r.All(string.IsNullOrWhiteSpace)) continue;

            var reg = Get(r, iReg)?.ToUpperInvariant();
            var name = Get(r, iName);
            if (reg is null || name is null)
            {
                errors.Add($"Row {line}: registration number and full name are required.");
                continue;
            }
            if (reg.Length > 50 || name.Length > 150)
            {
                errors.Add($"Row {line}: the registration number or name is too long.");
                continue;
            }
            if (!seen.Add(reg))
            {
                errors.Add($"Row {line}: {reg} appears more than once in the file.");
                continue;
            }
  
            var email = Get(r, iEmail);
            if (email is not null && !IsEmail(email))
            {
                errors.Add($"Row {line}: \"{email}\" is not a valid email address.");
                continue;
            }

            var phone = Get(r, iPhone);
            if (phone is not null)
            {
                phone = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
                if (phone.Length < 7 || phone.Length > 20)
                {
                    errors.Add($"Row {line}: the phone number is not valid.");
                    continue;
                }
            }

            DateOnly? dob = null;
            var dobText = Get(r, iDob);
            if (dobText is not null)
            {
                if (DateOnly.TryParseExact(dobText, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    dob = d;
                else
                {
                    errors.Add($"Row {line}: date of birth \"{dobText}\" should look like 2004-03-25.");
                    continue;
                }
            }

            var group = Get(r, iGroup);
            if (group is { Length: > 100 }) group = group[..100];

            rows.Add(new RosterRow(line, reg, name, email, phone, dob, group));
        }

        await using var db = await factory.CreateDbContextAsync();
        var regs = rows.Select(x => x.RegistrationNumber).ToList();
        var existing = await db.RosterEntries.CountAsync(e => e.OrganizationId == orgId && regs.Contains(e.RegistrationNumber));


        var total = await db.RosterEntries.CountAsync(e => e.OrganizationId == orgId);
        var newCount = rows.Count - existing;
        if (total + newCount > Limits.MaxRosterPerOrganisation)
        {
            errors.Add($"This file would take your roster past the limit of {Limits.MaxRosterPerOrganisation} people.");
            return new(new List<RosterRow>(), errors, 0, 0, 0);
        }

        return new(rows, errors, newCount, existing, rows.Count(x => x.Email is null && x.Phone is null));
    }

    public async Task<(int Added, int Updated)> ImportAsync(string adminId, List<RosterRow> rows)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var regs = rows.Select(r => r.RegistrationNumber).ToList();
        var existing = await db.RosterEntries
            .Where(e => e.OrganizationId == orgId && regs.Contains(e.RegistrationNumber))
            .ToDictionaryAsync(e => e.RegistrationNumber);

        int added = 0, updated = 0;
        foreach (var r in rows)
        {
            if (!existing.TryGetValue(r.RegistrationNumber, out var entry))
            {
                entry = new RosterEntry { OrganizationId = orgId, RegistrationNumber = r.RegistrationNumber };
                db.RosterEntries.Add(entry);
                added++;
            }
            else updated++;

            entry.FullName = r.FullName;
            entry.Email = r.Email;
            entry.PhoneNumber = r.Phone;
            entry.DateOfBirth = r.DateOfBirth;
            entry.GroupName = r.Group;
            entry.ImportedAt = DateTime.UtcNow;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = orgId,
            Action = "ImportRoster",
            EntityType = "Roster",
            Details = $"{added} added, {updated} updated"
        });
        await db.SaveChangesAsync();
        return (added, updated);
    }
    public async Task<string?> SetBlockedAsync(string adminId, int id, bool blocked)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.RosterEntries.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (e is null) return "Entry not found.";

        e.Blocked = blocked;
        if (blocked)
        {
            e.ClaimedByVoterProfileId = null;      // removing access also ends their membership
            e.ClaimedAt = null;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = orgId,
            Action = blocked ? "RemoveMemberAccess" : "AllowMemberAgain",
            EntityType = "Roster",
            EntityId = id.ToString(),
            Details = $"{e.FullName} ({e.RegistrationNumber})"
        });
        await db.SaveChangesAsync();
        return null;
    }
    public async Task<RosterPage> GetPageAsync(string? search, int page)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var q = db.RosterEntries.AsNoTracking().Where(e => e.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.RegistrationNumber.Contains(s) || e.FullName.Contains(s)
                          || (e.Email != null && e.Email.Contains(s)));
        }

        var total = await q.CountAsync();
        var items = await q.OrderBy(e => e.FullName)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        return new(items, total);
    }

    public async Task<string?> DeleteAsync(string adminId, int id)
    {
        var orgId = await org.RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        var e = await db.RosterEntries.FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == orgId);
        if (e is null) return "Entry not found.";
        if (e.ClaimedByVoterProfileId is not null)
            return "This person has already registered, so the entry can't be deleted.";

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            OrganizationId = orgId,
            Action = "DeleteRosterEntry",
            EntityType = "Roster",
            EntityId = id.ToString(),
            Details = $"{e.FullName} ({e.RegistrationNumber})"
        });
        db.RosterEntries.Remove(e);
        await db.SaveChangesAsync();
        return null;
    }

    private static bool IsEmail(string s) =>
        s.Length <= 200 && System.Net.Mail.MailAddress.TryCreate(s, out var a) && a.Address == s;

    // Small CSV reader that understands quoted fields
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        text = text.TrimStart('\uFEFF');

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}