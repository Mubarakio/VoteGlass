using System.ComponentModel.DataAnnotations;
using VoteGlass.Services;

namespace VoteGlass.Models;

public class ElectionInput
{
    [Required(ErrorMessage = "Title is required."), MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Start time is required.")]
    public DateTime? StartTime { get; set; }

    [Required(ErrorMessage = "End time is required.")]
    public DateTime? EndTime { get; set; }
    [Range(0, 120, ErrorMessage = "Enter an age between 0 and 120 (0 means no limit).")]
    public int MinimumAge { get; set; } = 18;
    public bool OpenToEveryone { get; set; } = true;
    public List<int> GroupIds { get; set; } = new();

    public ResultVisibility ResultVisibility { get; set; } = ResultVisibility.Live;

    [Required]
    public string AccentColor { get; set; } = "#7c9cff";

    public static ElectionInput From(Election e) => new()
    {
        Title = e.Title,
        Description = e.Description,
        StartTime = AppTime.ToLocal(e.StartTime),
        EndTime = AppTime.ToLocal(e.EndTime),
        MinimumAge = e.MinimumAge,
        OpenToEveryone = e.OpenToEveryone,
        GroupIds = e.Groups.Select(g => g.Id).ToList(),
        ResultVisibility = e.ResultVisibility,
        AccentColor = e.AccentColor
    };
}