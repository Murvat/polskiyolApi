using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {

    }
    public DbSet<Teacher> Teachers { get; set; }
    public DbSet<Student> Students { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Lesson> Lessons { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<FeedPost> FeedPosts { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);
        modelBuilder.Entity<Payment>()
.Property(p => p.ExpectedAmount)
.HasPrecision(18, 2);
        modelBuilder.Entity<Teacher>()
            .HasOne(t => t.User)
            .WithOne()
            .HasForeignKey<Teacher>(t => t.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.SecurityStamp)
                .IsRequired();
            entity.Property(t => t.TokenHash)
                .IsRequired()
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();

            entity.HasIndex(t => t.TokenHash)
                .IsUnique();

            entity.HasIndex(t => t.UserId);

            entity.HasIndex(t => t.FamilyId);

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.ReplacedByToken)
                .WithOne()
                .HasForeignKey<RefreshToken>(
                    t => t.ReplacedByTokenId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.Property(t => t.RowVersion)
                .IsRowVersion();
        });
        modelBuilder.Entity<Student>()
            .HasOne(s => s.User)
            .WithOne()
            .HasForeignKey<Student>(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<Payment>()
       .HasIndex(p => new
       {
           p.StudentId,
           p.Year,
           p.Month
       })
       .IsUnique();
    }
}