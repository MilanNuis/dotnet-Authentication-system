using System.Security.Claims;
using AuthApi.Api.Data;
using AuthApi.Api.DTOs;
using AuthApi.Api.Models;
using AuthApi.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly TokenService _tokenService;

    // Constructor injection = Dependency Injection.
    // ASP.NET creates AuthController and passes these services in.
    // Laravel: public function __construct(AppDbContext $db, ...)
    public AuthController(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        TokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/register
    // -------------------------------------------------------------------------
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var emailExists = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (emailExists)
        {
            return BadRequest(new { message = "Email already exists" });
        }

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Role = "user",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // PasswordHasher produces a salted, iterated hash (like Laravel Hash::make).
        // Never store plaintext or a plain SHA-256 of the password.
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "User registered successfully" });
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/login
    // -------------------------------------------------------------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        // Same 401 whether email is missing or password is wrong —
        // do not leak which one failed (user enumeration).
        if (user is null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var accessToken = _tokenService.CreateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var session = new Session
        {
            UserId = user.Id,
            // Store only the hash — raw refresh token goes to the client only.
            RefreshTokenHash = _tokenService.HashRefreshToken(refreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
        });
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/refresh  (refresh-token rotation)
    // -------------------------------------------------------------------------
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);

        var session = await _db.Sessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash);

        if (session is null
            || session.RevokedAt is not null
            || session.ExpiresAt <= DateTime.UtcNow)
        {
            return Unauthorized(new { message = "Invalid refresh token" });
        }

        // Rotation: replace the stored hash so the old raw token stops working.
        var newRefreshToken = _tokenService.GenerateRefreshToken();
        session.RefreshTokenHash = _tokenService.HashRefreshToken(newRefreshToken);
        session.ExpiresAt = DateTime.UtcNow.AddDays(7);

        await _db.SaveChangesAsync();

        var accessToken = _tokenService.CreateAccessToken(session.User);

        return Ok(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
        });
    }

    // -------------------------------------------------------------------------
    // POST /api/auth/logout
    // -------------------------------------------------------------------------
    // Revokes the session. Access JWTs are NOT blacklisted — they expire in 15 min.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);

        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash);

        if (session is null)
        {
            // Idempotent: already gone / unknown token → still 204.
            return NoContent();
        }

        session.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // -------------------------------------------------------------------------
    // GET /api/auth/me
    // -------------------------------------------------------------------------
    // [Authorize] = require a valid JWT (Laravel: auth:sanctum / auth:api middleware).
    // After UseAuthentication runs, HttpContext.User is a ClaimsPrincipal filled
    // from the JWT claims we created in TokenService.
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _db.Users.FindAsync(userId.Value);
        if (user is null)
        {
            return Unauthorized();
        }

        // Safe projection — never return PasswordHash.
        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/auth/sessions
    // -------------------------------------------------------------------------
    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var sessions = await _db.Sessions
            .Where(s => s.UserId == userId.Value
                        && s.RevokedAt == null
                        && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                id = s.Id,
                createdAt = s.CreatedAt,
                expiresAt = s.ExpiresAt,
            })
            .ToListAsync();

        return Ok(sessions);
    }

    // -------------------------------------------------------------------------
    // DELETE /api/auth/sessions/{id}
    // -------------------------------------------------------------------------
    [Authorize]
    [HttpDelete("sessions/{id:int}")]
    public async Task<IActionResult> RevokeSession(int id)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Scope by UserId so a user can never revoke someone else's session.
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId.Value);

        if (session is null)
        {
            return NotFound();
        }

        session.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Reads the user id claim placed in the JWT at login/refresh time.
    /// User here is ClaimsPrincipal (HttpContext.User), not the EF User model.
    /// Laravel: auth()->id()
    /// </summary>
    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
