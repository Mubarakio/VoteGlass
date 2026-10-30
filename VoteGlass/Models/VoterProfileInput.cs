using System.ComponentModel.DataAnnotations;

namespace VoteGlass.Models;

public class VoterProfileInput
{
    [Required(ErrorMessage = "Full name is required."), MaxLength(150)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^\+?[0-9\s\-]{7,20}$", ErrorMessage = "Enter a valid phone number.")]
    public string PhoneNumber { get; set; } = "";

    [Required(ErrorMessage = "Please select a gender.")]
    public string Gender { get; set; } = "";

    [Required(ErrorMessage = "Date of birth is required.")]
    public DateOnly? DateOfBirth { get; set; }

    [Required(ErrorMessage = "Address is required."), MaxLength(300)]
    public string Address { get; set; } = "";

    public static VoterProfileInput From(VoterProfile p) => new()
    {
        FullName = p.FullName,
        PhoneNumber = p.PhoneNumber,
        Gender = p.Gender,
        DateOfBirth = p.DateOfBirth,
        Address = p.Address
    };
}