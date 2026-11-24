using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.Property(m => m.FileName)
            .HasMaxLength(255);

        builder.Property(m => m.Description)
            .HasMaxLength(1000);

        builder.Property(m => m.FileStorageKey)
            .HasMaxLength(500);

        builder.Property(m => m.CoverStorageKey)
            .HasMaxLength(500);

        builder.HasOne(m => m.Teacher)
            .WithMany(t => t.Materials)
            .HasForeignKey(m => m.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}