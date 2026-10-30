using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

public enum OrgRole { Owner, Admin }

public class Organization
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public bool IsSuspended { get; set; }
    [MaxLength(20)] public string? JoinCode { get; set; }       // random code used in join links
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<OrganizationMember> Members { get; set; } = new();
}

// Who runs an organisation
public class OrganizationMember
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    [Required] public string UserId { get; set; } = "";
    public OrgRole Role { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}