public class Material
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public Teacher Teacher { get; set; }
    public string FileName { get; set; }
    public string? Description { get; set; }
    public string FileStorageKey { get; set; }
    public string? CoverStorageKey { get; set; }
}