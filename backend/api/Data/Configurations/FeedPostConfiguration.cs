using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class FeedPostConfiguration : IEntityTypeConfiguration<FeedPost>
{
    public void Configure(EntityTypeBuilder<FeedPost> builder)
    {
        builder.Property(p => p.Title)
            .HasMaxLength(200);

        builder.Property(p => p.Content)
            .HasMaxLength(4000);

        builder.Property(p => p.PostImageKey)
            .HasMaxLength(500);

        builder.HasOne(p => p.Teacher)
            .WithMany(t => t.FeedPosts)
            .HasForeignKey(p => p.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}