namespace VoteGlass.Services;

// Limits that protect the platform from misuse. Change the numbers here.
public static class Limits
{
    public const int MaxOrganisationsOwnedPerUser = 5;
    public const int MaxRosterPerOrganisation = 2000;
    public const int MaxElectionsPerOrganisation = 50;
    public const int MaxTeamPerOrganisation = 10;
    public const int MaxInvitesPerDay = 300;
}