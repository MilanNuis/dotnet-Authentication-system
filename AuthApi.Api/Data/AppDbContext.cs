using AuthApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Unique email — same idea as Laravel Schema::unique('email')
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // User 1—* Session (Laravel: hasMany / belongsTo)
        modelBuilder.Entity<Session>()
            .HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Lookup sessions by hashed refresh token
        modelBuilder.Entity<Session>()
            .HasIndex(s => s.RefreshTokenHash)
            .IsUnique();
    }
}
