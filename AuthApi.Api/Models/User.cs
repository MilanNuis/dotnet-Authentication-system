namespace AuthApi.Api.Models;

public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Never return this to the client. Verified with PasswordHasher<User>.
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "user";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // EF Core navigation: one User has many Sessions (like Laravel hasMany).
    public List<Session> Sessions { get; set; } = [];
}
