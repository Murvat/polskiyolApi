public class MaterialResponse
{
    public int Id { get; set; }
    public int TeacherId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool HasCover { get; set; }

    public static MaterialResponse FromMaterial(Material material)
    {
        return new MaterialResponse
        {
            Id = material.Id,
            TeacherId = material.TeacherId,
            FileName = material.FileName,
            Description = material.Description,
            HasCover = !string.IsNullOrEmpty(material.CoverStorageKey)
        };
    }
}