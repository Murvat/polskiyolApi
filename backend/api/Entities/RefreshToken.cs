public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;
    public string SecurityStamp { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = null!;
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
    public DateTime SessionExpiresAtUtc { get; set; }
    public RefreshToken? ReplacedByToken { get; set; }
}