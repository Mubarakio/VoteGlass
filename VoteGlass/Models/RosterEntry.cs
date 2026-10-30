using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

// One person on the admin's list of allowed voters
public class RosterEntry
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    [Required, MaxLength(50)] public string RegistrationNumber { get; set; } = "";
    [Required, MaxLength(150)] public string FullName { get; set; } = "";
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(30)] public string? PhoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [MaxLength(100)] public string? GroupName { get; set; }
    public int? ClaimedByVoterProfileId { get; set; }     // set in Step 9 when the person verifies
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime? InvitedAt { get; set; }
    public int InviteCount { get; set; }
    public bool Blocked { get; set; }                  // the organiser removed this person's access
    public DateTime? ClaimedAt { get; set; }           // when the person joined
}