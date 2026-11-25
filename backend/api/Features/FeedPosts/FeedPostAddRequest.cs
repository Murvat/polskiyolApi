using System.ComponentModel.DataAnnotations;

public class FeedPostAddRequest
{
    [Required]
    [StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Content { get; set; } = string.Empty;

}
