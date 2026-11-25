
using System.ComponentModel.DataAnnotations;

public class MessageAddRequest
{
    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;
}
