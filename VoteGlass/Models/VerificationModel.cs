using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

// One emailed code. Only a scrambled (hashed) version is stored.
public class VerificationCode
{
    public int Id { get; set; }
    public int VoterProfileId { get; set; }
    public int RosterEntryId { get; set; }              // the roster entry the code was sent for
    [Required, MaxLength(100)] public string Salt { get; set; } = "";
    [Required, MaxLength(100)] public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

// Test mode only: emails that would have been sent
public class EmailMessage
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string ToAddress { get; set; } = "";
    [Required, MaxLength(200)] public string Subject { get; set; } = "";
    [Required] public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}