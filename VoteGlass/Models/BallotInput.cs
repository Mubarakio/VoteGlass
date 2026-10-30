using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

public class PositionInput
{
    [Required(ErrorMessage = "Position title is required."), MaxLength(150)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Range(1, 20, ErrorMessage = "Enter a number between 1 and 20.")]
    public int MaxSelections { get; set; } = 1;

    public bool AllowAbstain { get; set; } = true;

    public TieHandling TieHandling { get; set; } = TieHandling.DeclareTie;

    public static PositionInput From(Position p) => new()
    {
        Title = p.Title,
        Description = p.Description,
        MaxSelections = p.MaxSelections,
        AllowAbstain = p.AllowAbstain,
        TieHandling = p.TieHandling
    };
}

public class CandidateInput
{
    [Required(ErrorMessage = "Candidate name is required."), MaxLength(150)]
    public string FullName { get; set; } = "";

    [MaxLength(2000)]
    public string? Manifesto { get; set; }

    public static CandidateInput From(Candidate c) => new()
    {
        FullName = c.FullName,
        Manifesto = c.Manifesto
    };
}