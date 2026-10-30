using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

public enum ElectionStatus { Draft, Scheduled, Live, Closed }
public enum ResultVisibility { Live, AfterClose, AfterPublish }
public enum TieHandling { DeclareTie, AdminDecides, RunOff }
public enum VoterStatus { Pending, Approved, Rejected }

// A voter's profile, linked to their Identity login account
public class VoterProfile
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = "";          // FK to ApplicationUser
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [Required, Phone, MaxLength(30)] public string PhoneNumber { get; set; } = "";
    [Required, MaxLength(300)] public string Address { get; set; } = "";
    [Required, MaxLength(20)] public string Gender { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public string? PhotoPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VoterGroup> Groups { get; set; } = new List<VoterGroup>();
}

// e.g. "Computer Science", "300 Level"
public class VoterGroup
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public string? Description { get; set; }

    public ICollection<VoterProfile> Members { get; set; } = new List<VoterProfile>();
    public ICollection<Election> Elections { get; set; } = new List<Election>();
}

public class Election
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }                      // store in UTC
    public DateTime EndTime { get; set; }                        // store in UTC
    public int MinimumAge { get; set; } = 18;                    // 0 = no age limit
    public bool OpenToEveryone { get; set; } = true;             // false = only the selected groups
    public ElectionStatus Status { get; set; } = ElectionStatus.Draft;
    public ResultVisibility ResultVisibility { get; set; } = ResultVisibility.Live;
    public bool ResultsPublished { get; set; }                   // used when AfterPublish
    [MaxLength(9)] public string AccentColor { get; set; } = "#7c9cff";
    public string? BannerPath { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Position> Positions { get; set; } = new List<Position>();
    public ICollection<EligibleVoter> EligibleVoters { get; set; } = new List<EligibleVoter>();
    public ICollection<VoterGroup> Groups { get; set; } = new List<VoterGroup>();
}

public class Position
{
    public int Id { get; set; }
    public int ElectionId { get; set; }
    public Election? Election { get; set; }
    [Required, MaxLength(150)] public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int MaxSelections { get; set; } = 1;
    public bool AllowAbstain { get; set; } = true;
    public TieHandling TieHandling { get; set; } = TieHandling.DeclareTie;
    public int DisplayOrder { get; set; }
    [MaxLength(200)] public string? TieBreakWinners { get; set; }     // candidate ids the admin picked to settle a tie, like "4,7"

    public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
}

public class Candidate
{
    public int Id { get; set; }
    public int PositionId { get; set; }
    public Position? Position { get; set; }
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    public string? Manifesto { get; set; }
    public string? PhotoPath { get; set; }
    public int DisplayOrder { get; set; }
}

// Which voters are allowed in which election
public class EligibleVoter
{
    public int Id { get; set; }
    public int ElectionId { get; set; }
    public Election? Election { get; set; }
    public int VoterProfileId { get; set; }
    public VoterProfile? VoterProfile { get; set; }

}

// WHO voted (turnout). Never says what they voted for.
public class VoteRecord
{
    public int Id { get; set; }
    public int ElectionId { get; set; }
    public int PositionId { get; set; }
    public int VoterProfileId { get; set; }
    public DateTime CastAt { get; set; } = DateTime.UtcNow;
}

// WHAT was voted for. Deliberately has no voter ID and no timestamp,
// so a ballot can never be traced back to a person.
// CandidateId = null means "abstain".
public class Ballot
{
    public int Id { get; set; }
    public int ElectionId { get; set; }
    public int PositionId { get; set; }
    public int? CandidateId { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class Announcement
{
    public int Id { get; set; }
    public int? ElectionId { get; set; }                         // null = site-wide
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required] public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}