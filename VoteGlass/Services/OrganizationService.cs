using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using VoteGlass.Data;
using VoteGlass.Models;

namespace VoteGlass.Services;

public record OrgAccess(int OrganizationId, string OrganizationName, OrgRole Role, string? JoinCode);
public record OrgListItem(int Id, string Name, OrgRole Role, bool IsCurrent, bool IsSuspended, string? JoinCode);
public record TeamMember(int MemberId, string UserId, string Email, OrgRole Role);

public class OrganizationService(IDbContextFactory<ApplicationDbContext> factory)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no look-alike characters

    // A random code that can't be guessed. It goes in join links.
    public static string NewJoinCode()
    {
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    // The organisation this person is working in right now, if they run it and it isn't suspended
    public async Task<OrgAccess?> GetAccessAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();

          var currentId = await db.Users.Where(u => u.Id == userId)
            .Select(u => u.CurrentOrganizationId).FirstOrDefaultAsync();
        if (currentId is null) return null;

        var row = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.OrganizationId == currentId)
            .Select(m => new { m.OrganizationId, m.Organization!.Name, m.Organization.IsSuspended, m.Organization.JoinCode, m.Role })
            .FirstOrDefaultAsync();

        return row is null || row.IsSuspended
            ? null
            : new OrgAccess(row.OrganizationId, row.Name, row.Role, row.JoinCode);
    }

    public async Task<List<OrgListItem>> GetMineAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var currentId = await db.Users.Where(u => u.Id == userId)
            .Select(u => u.CurrentOrganizationId).FirstOrDefaultAsync();

        var rows = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.OrganizationId, m.Organization!.Name, m.Organization.IsSuspended, m.Organization.JoinCode, m.Role })
            .ToListAsync();

        return rows
            .Select(r => new OrgListItem(r.OrganizationId, r.Name, r.Role, r.OrganizationId == currentId, r.IsSuspended, r.JoinCode))
            .OrderBy(r => r.Name)
            .ToList();
    }

    public async Task<(int? Id, string? Error)> CreateAsync(string userId, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3) return (null, "Enter a name of at least 3 characters.");
        if (name.Length > 150) return (null, "That name is too long.");

        await using var db = await factory.CreateDbContextAsync();

        var owned = await db.OrganizationMembers.CountAsync(m => m.UserId == userId && m.Role == OrgRole.Owner);
        if (owned >= Limits.MaxOrganisationsOwnedPerUser)
            return (null, $"You can own up to {Limits.MaxOrganisationsOwnedPerUser} organisations.");

        var org = new Organization { Name = name, CreatedByUserId = userId, JoinCode = NewJoinCode() };
        org.Members.Add(new OrganizationMember { UserId = userId, Role = OrgRole.Owner });
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.CurrentOrganizationId = org.Id;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = org.Id,
            Action = "CreateOrganisation",
            EntityType = "Organization",
            EntityId = org.Id.ToString(),
            Details = org.Name
        });
        await db.SaveChangesAsync();
        return (org.Id, null);
    }

    public async Task<string?> SwitchAsync(string userId, int organizationId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var allowed = await db.OrganizationMembers.AnyAsync(m =>
            m.UserId == userId && m.OrganizationId == organizationId && !m.Organization!.IsSuspended);
        if (!allowed) return "You don't have access to that organisation.";

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.CurrentOrganizationId = organizationId;
        await db.SaveChangesAsync();
        return null;
    }

    // Makes every old join link stop working
    public async Task<string?> ResetJoinCodeAsync(string userId, int organizationId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db, userId, organizationId)) return "Only the owner can change the join link.";

        var org = await db.Organizations.FirstAsync(o => o.Id == organizationId);
        org.JoinCode = NewJoinCode();

        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = organizationId,
            Action = "ResetJoinCode",
            EntityType = "Organization",
            EntityId = organizationId.ToString(),
            Details = org.Name
        });
        await db.SaveChangesAsync();
        return null;
    }

    // Gives a code to organisations created before join codes existed
    public async Task BackfillJoinCodesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var orgs = await db.Organizations.Where(o => o.JoinCode == null).ToListAsync();
        if (orgs.Count == 0) return;

        foreach (var o in orgs) o.JoinCode = NewJoinCode();
        await db.SaveChangesAsync();
    }

    // ----- Team (owner only) -----
    public async Task<List<TeamMember>> GetTeamAsync(string userId, int organizationId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db, userId, organizationId)) return new();

        var rows = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .Join(db.Users, m => m.UserId, u => u.Id,
                (m, u) => new { m.Id, m.UserId, u.Email, u.UserName, m.Role })
            .ToListAsync();

        return rows
            .Select(r => new TeamMember(r.Id, r.UserId, r.Email ?? r.UserName ?? "(no email)", r.Role))
            .OrderBy(t => t.Role).ThenBy(t => t.Email)
            .ToList();
    }

    public async Task<string?> AddAdminAsync(string userId, int organizationId, string? email)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db, userId, organizationId)) return "Only the owner can manage the team.";

        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return "Enter an email address.";

        if (await db.OrganizationMembers.CountAsync(m => m.OrganizationId == organizationId) >= Limits.MaxTeamPerOrganisation)
            return $"A team can have up to {Limits.MaxTeamPerOrganisation} people.";

        var normalized = email.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
        if (user is null) return "No account uses that email. Ask the person to register first, then try again.";

        if (await db.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user.Id))
            return "That person is already on the team.";

        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = user.Id,
            Role = OrgRole.Admin
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = organizationId,
            Action = "AddTeamAdmin",
            EntityType = "User",
            EntityId = user.Id,
            Details = user.Email
        });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> RemoveMemberAsync(string userId, int organizationId, int memberId)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await IsOwnerAsync(db, userId, organizationId)) return "Only the owner can manage the team.";

        var member = await db.OrganizationMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId);
        if (member is null) return "Team member not found.";
        if (member.Role == OrgRole.Owner) return "The owner can't be removed.";

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == member.UserId);
        if (user?.CurrentOrganizationId == organizationId) user.CurrentOrganizationId = null;

        db.OrganizationMembers.Remove(member);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            OrganizationId = organizationId,
            Action = "RemoveTeamAdmin",
            EntityType = "User",
            EntityId = member.UserId,
            Details = user?.Email
        });
        await db.SaveChangesAsync();
        return null;
    }

    private static Task<bool> IsOwnerAsync(ApplicationDbContext db, string userId, int organizationId) =>
        db.OrganizationMembers.AnyAsync(m =>
            m.OrganizationId == organizationId && m.UserId == userId &&
            m.Role == OrgRole.Owner && !m.Organization!.IsSuspended);
}

// Every organisation-scoped service asks this which organisation it may work in
public class OrgContext(AuthenticationStateProvider auth, OrganizationService orgs)
{
    public async Task<OrgAccess?> GetAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? null : await orgs.GetAccessAsync(userId);
    }

    public async Task<int> RequireAsync()
    {
        var access = await GetAsync() ?? throw new UnauthorizedAccessException("Choose an organisation first.");
        return access.OrganizationId;
    }
}

// "OrgAdmin": runs the organisation they're working in. "OrgOwner": is its owner.
public class OrgAdminRequirement(bool ownerOnly = false) : IAuthorizationRequirement
{
    public bool OwnerOnly { get; } = ownerOnly;
}

public class OrgAdminHandler(OrganizationService orgs) : AuthorizationHandler<OrgAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgAdminRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return;

        var access = await orgs.GetAccessAsync(userId);
        if (access is null) return;
        if (requirement.OwnerOnly && access.Role != OrgRole.Owner) return;

        context.Succeed(requirement);
    }
}