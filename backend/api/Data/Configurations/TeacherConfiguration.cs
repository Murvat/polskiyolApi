using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.Property(t => t.FirstName)
            .HasMaxLength(100);

        builder.Property(t => t.LastName)
            .HasMaxLength(100);

        builder.Property(t => t.PhoneNumber)
            .HasMaxLength(30);
    }
}