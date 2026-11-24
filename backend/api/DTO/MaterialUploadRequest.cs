
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

public class MaterialUploadRequest : IValidatableObject
{
    [Required]
    public IFormFile? File { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (File is null)
            yield break;

        if (File.Length == 0)
        {
            yield return new ValidationResult(
                "The uploaded file cannot be empty.",
                new[] { nameof(File) });
        }

        if (File.Length > 10 * 1024 * 1024)
        {
            yield return new ValidationResult(
                "The file cannot exceed 10 MB.",
                new[] { nameof(File) });
        }
    }
}

public class MaterialCoverUploadRequest : IValidatableObject
{
    [Required]
    public IFormFile? File { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (File is null)
            yield break;

        if (File.Length == 0)
        {
            yield return new ValidationResult(
                "The uploaded image cannot be empty.",
                new[] { nameof(File) });
        }

        if (File.Length > 5 * 1024 * 1024)
        {
            yield return new ValidationResult(
                "The image cannot exceed 5 MB.",
                new[] { nameof(File) });
        }
    }
}