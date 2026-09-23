namespace AuthApi.Api.Models;

public class Session
{
    public int Id { get; set; }

    public int UserId { get; set; }

    // EF Core navigation: Session belongs to one User (like Laravel belongsTo).
    public User User { get; set; } = null!;

    // SHA-256 hash of the raw refresh token. Raw token is never stored.
    public string RefreshTokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
