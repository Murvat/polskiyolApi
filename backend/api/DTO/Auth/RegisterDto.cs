using System.ComponentModel.DataAnnotations;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;
    [Required]
    [StringLength(1024)]
    public string Password { get; set; } = string.Empty;
    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    [Required]
    [StringLength(30)]
    public string PhoneNumber { get; set; } = string.Empty;
}