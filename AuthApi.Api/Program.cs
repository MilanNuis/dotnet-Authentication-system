using System.Text;
using AuthApi.Api.Data;
using AuthApi.Api.Models;
using AuthApi.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

// ---------------------------------------------------------------------------
// Dependency Injection (DI)
// ---------------------------------------------------------------------------
// ASP.NET Core builds a "service container". Controllers declare what they need
// in their constructor (AppDbContext, TokenService, PasswordHasher). The
// container creates and injects those instances automatically.
//
// Laravel equivalent: binding in a ServiceProvider + constructor injection,
// or resolving via app(TokenService::class).
// ---------------------------------------------------------------------------

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    );
});

// Singleton lifetime is fine: PasswordHasher is stateless.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// Scoped: one TokenService per HTTP request (reads IConfiguration).
builder.Services.AddScoped<TokenService>();

// ---------------------------------------------------------------------------
// AddAuthentication + AddJwtBearer
// ---------------------------------------------------------------------------
// Registers the authentication system and tells it: "when a request has a
// Bearer token, validate it as a JWT using this key/issuer/audience".
//
// Laravel equivalent: configuring the auth guard (Sanctum / JWT middleware)
// in config/auth.php + middleware registration.
// ---------------------------------------------------------------------------
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Prefer short-lived access tokens; clock skew would extend them.
            ClockSkew = TimeSpan.Zero,
        };
    });

// Registers authorization services used by [Authorize].
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ---------------------------------------------------------------------------
// UseAuthentication / UseAuthorization
// ---------------------------------------------------------------------------
// Middleware order matters:
// 1. UseAuthentication — reads the Authorization: Bearer <jwt> header,
//    validates the token, and fills HttpContext.User (ClaimsPrincipal).
// 2. UseAuthorization — checks [Authorize] on the endpoint against that User.
//
// Laravel equivalent: auth middleware running before the controller action.
// ---------------------------------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
