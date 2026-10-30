using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VoteGlass.Models;

namespace VoteGlass.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<VoterProfile> VoterProfiles => Set<VoterProfile>();
    public DbSet<VoterGroup> VoterGroups => Set<VoterGroup>();
    public DbSet<Election> Elections => Set<Election>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<EligibleVoter> EligibleVoters => Set<EligibleVoter>();
    public DbSet<VoteRecord> VoteRecords => Set<VoteRecord>();
    public DbSet<Ballot> Ballots => Set<Ballot>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // One profile per login account
        b.Entity<VoterProfile>().HasIndex(v => v.UserId).IsUnique();
        b.Entity<VoterProfile>().HasIndex(v => v.PhoneNumber).IsUnique();
        b.Entity<VoterProfile>().HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Restrict);

        // A voter can only be added to an election once
        b.Entity<EligibleVoter>().HasIndex(e => new { e.ElectionId, e.VoterProfileId }).IsUnique();
        b.Entity<EligibleVoter>().HasOne(e => e.VoterProfile).WithMany()
            .HasForeignKey(e => e.VoterProfileId).OnDelete(DeleteBehavior.Restrict);

        // Database-level guarantee: one vote per voter per position
        b.Entity<VoteRecord>().HasIndex(v => new { v.PositionId, v.VoterProfileId }).IsUnique();
        b.Entity<VoteRecord>().HasOne<Election>().WithMany()
            .HasForeignKey(v => v.ElectionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VoteRecord>().HasOne<Position>().WithMany()
            .HasForeignKey(v => v.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VoteRecord>().HasOne<VoterProfile>().WithMany()
            .HasForeignKey(v => v.VoterProfileId).OnDelete(DeleteBehavior.Restrict);

        // Ballots: restrict deletes to avoid SQL Server multiple cascade path errors
        b.Entity<Ballot>().HasIndex(x => new { x.PositionId, x.CandidateId });
        b.Entity<Ballot>().HasOne<Election>().WithMany()
            .HasForeignKey(x => x.ElectionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Ballot>().HasOne<Position>().WithMany()
            .HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Ballot>().HasOne<Candidate>().WithMany()
            .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Announcement>().HasOne<Election>().WithMany()
            .HasForeignKey(a => a.ElectionId).OnDelete(DeleteBehavior.Cascade);

        // Registration numbers and group names only need to be unique inside one organisation
        b.Entity<RosterEntry>().HasIndex(r => new { r.OrganizationId, r.RegistrationNumber }).IsUnique();
        b.Entity<RosterEntry>().HasIndex(r => new { r.OrganizationId, r.ClaimedByVoterProfileId }).IsUnique();
        b.Entity<VoterGroup>().HasIndex(g => new { g.OrganizationId, g.Name }).IsUnique();

        b.Entity<Election>().HasOne(e => e.Organization).WithMany()
            .HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RosterEntry>().HasOne<Organization>().WithMany()
            .HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VoterGroup>().HasOne<Organization>().WithMany()
            .HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<OrganizationMember>().HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
        b.Entity<OrganizationMember>().HasOne(m => m.Organization).WithMany(o => o.Members)
            .HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrganizationMember>().HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<VerificationCode>().HasIndex(c => c.VoterProfileId);
        b.Entity<VerificationCode>().HasOne<VoterProfile>().WithMany()
            .HasForeignKey(c => c.VoterProfileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Organization>().HasIndex(o => o.JoinCode).IsUnique();
    }
}