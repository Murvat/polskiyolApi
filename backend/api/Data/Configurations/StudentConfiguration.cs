using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.Property(s => s.FirstName)
    .HasMaxLength(100);

        builder.Property(s => s.LastName)
            .HasMaxLength(100);

        builder.Property(s => s.PhoneNumber)
            .HasMaxLength(30);

        builder.HasOne(s => s.Teacher)
            .WithMany(t => t.Students)
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}