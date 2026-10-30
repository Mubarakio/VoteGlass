using Microsoft.AspNetCore.Identity;

namespace VoteGlass.Data;

public class ApplicationUser : IdentityUser
{
    public int? CurrentOrganizationId { get; set; }     // the organisation this person is working in right now
}