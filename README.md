# AuthApi

Leerproject: ASP.NET Core API met JWT access tokens, refresh tokens/sessions, en PostgreSQL (EF Core).

Vergelijking met Laravel staat waar dat helpt.

---

## Snel starten

```bash
# 1. Ga naar het API-project
cd AuthApi.Api

# 2. Zorg dat PostgreSQL draait en de connection string klopt
#    → appsettings.json → ConnectionStrings:DefaultConnection

# 3. Database migreren
dotnet ef database update

# 4. API starten (http://localhost:5165)
dotnet run --launch-profile http
```

---

## Handige commands

| Command | Wat doet het? |
|---|---|
| `dotnet run` | Start de API |
| `dotnet run --launch-profile http` | Start alleen op HTTP (poort 5165) |
| `dotnet build` | Compileert de code (fouten checken) |
| `dotnet watch run` | Herstart automatisch bij codewijzigingen |
| `dotnet add package Naam` | Voegt een NuGet-package toe (≈ Composer `require`) |
| `dotnet ef migrations add Naam` | Maakt een nieuwe migratie van modelwijzigingen |
| `dotnet ef database update` | Past migraties toe op PostgreSQL |
| `dotnet ef database drop --force` | Verwijdert de database (handig om schoon te beginnen) |
| `dotnet ef migrations list` | Toont welke migraties er zijn / toegepast |

Migratie-workflow (zoals Laravel `migrate:make` + `migrate`):

```bash
# Model of DbContext aangepast? → migratie maken + toepassen
dotnet ef migrations add BeschrijvendeNaam
dotnet ef database update
```

---

## Projectstructuur

```
AuthApi/
├── AuthApi.slnx                 # Solution-bestand (meerdere projecten kunnen hierin)
└── AuthApi.Api/                 # Het web-API project
    ├── Program.cs               # Opstarten: DI, auth, middleware, routes
    ├── appsettings.json         # Config (connection string, JWT key, …)
    ├── AuthApi.Api.csproj       # Packages & target framework (≈ composer.json)
    ├── Controllers/             # HTTP endpoints (≈ app/Http/Controllers)
    │   └── AuthController.cs
    ├── Models/                  # Database-entiteiten (≈ Eloquent models)
    │   ├── User.cs
    │   └── Session.cs
    ├── DTOs/                    # Request/response shapes (≈ Form Requests / Resources)
    │   ├── LoginRequest.cs
    │   ├── RegisterRequest.cs
    │   └── …
    ├── Data/
    │   └── AppDbContext.cs      # EF Core = jouw "database bridge" (≈ Eloquent + schema config)
    ├── Services/
    │   └── TokenService.cs      # Business-logica die geen HTTP is (JWT / refresh tokens)
    ├── Migrations/              # Database-wijzigingen (≈ database/migrations)
    └── Properties/
        └── launchSettings.json  # Poorten & profiles voor `dotnet run`
```

### Wat hoort waar?

| Map | Rol | Laravel-achtig |
|---|---|---|
| `Controllers/` | Ontvangt HTTP, roept DB/services aan, stuurt JSON terug | Controllers |
| `Models/` | Tabellen/kolommen als C#-klassen | Eloquent models |
| `DTOs/` | Wat de client mag sturen/ontvangen | Form Request / API Resource |
| `Data/` | `DbContext` + relaties/indexes | DB-laag + migrations-config |
| `Services/` | Herbruikbare logica zonder HTTP | Service classes |
| `Program.cs` | App bootstrappen + middleware | `bootstrap/app.php` + providers |

---

## Endpoints (nu)

| Method | Route | Auth? | Doel |
|---|---|---|---|
| `POST` | `/api/auth/register` | Nee | Account aanmaken |
| `POST` | `/api/auth/login` | Nee | Access + refresh token |
| `POST` | `/api/auth/refresh` | Nee* | Nieuwe tokens (refresh rotation) |
| `POST` | `/api/auth/logout` | Nee* | Session intrekken via refresh token |
| `GET` | `/api/auth/me` | JWT | Huidige user |
| `GET` | `/api/auth/sessions` | JWT | Actieve sessions |
| `DELETE` | `/api/auth/sessions/{id}` | JWT | Eigen session intrekken |

\* Gebruikt een refresh token in de body, geen Bearer JWT.

---

## Nieuwe route toevoegen

### Optie A — methode in bestaande controller

1. Open `Controllers/AuthController.cs` (of een andere controller).
2. Voeg een methode toe met attributes:

```csharp
[Authorize]                 // weglaten = publiek (zoals zonder auth middleware)
[HttpGet("profile")]        // → GET /api/auth/profile  (want Route is "api/auth")
public async Task<IActionResult> Profile()
{
    return Ok(new { message = "Hallo" });
}
```

3. Klaar — `MapControllers()` in `Program.cs` ontdekt dit automatisch.

### Optie B — nieuwe controller (nieuwe resource)

1. Maak `Controllers/ProductsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthApi.Api.Controllers;

[ApiController]
[Route("api/products")]          // basispad
public class ProductsController : ControllerBase
{
    [HttpGet]                    // GET /api/products
    public IActionResult Index()
    {
        return Ok(new[] { "appel", "peer" });
    }

    [Authorize]
    [HttpPost]                   // POST /api/products  (alleen met JWT)
    public IActionResult Store([FromBody] CreateProductRequest request)
    {
        return Ok(request);
    }

    [HttpGet("{id:int}")]        // GET /api/products/5
    public IActionResult Show(int id)
    {
        return Ok(new { id });
    }
}
```

2. Optioneel: DTO in `DTOs/CreateProductRequest.cs`.
3. Optioneel: Model + `DbSet` in `AppDbContext` + migratie.

### Laravel → ASP.NET route-mapping

| Laravel | ASP.NET |
|---|---|
| `Route::get('/api/products', …)` | `[HttpGet]` op controller-methode |
| `Route::post(…)` | `[HttpPost]` |
| `->middleware('auth:sanctum')` | `[Authorize]` |
| `Route::prefix('api')` | `[Route("api/products")]` |
| `$request->user()` / `auth()->id()` | `User.FindFirstValue(ClaimTypes.NameIdentifier)` |

---

## Wat doet wat in een `.cs` bestand?

### 1. Controller (HTTP-laag)

```csharp
using Microsoft.AspNetCore.Mvc;   // attributes & IActionResult

namespace AuthApi.Api.Controllers; // "map" voor de class (zoals PHP namespace)

[ApiController]                   // API-gedrag: automatische modelvalidatie, etc.
[Route("api/auth")]                // basis-URL voor alle methodes hieronder
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;   // veld: database toegang

    // Constructor = Dependency Injection
    // ASP.NET vult _db automatisch in (geregistreerd in Program.cs)
    public AuthController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("login")]            // HTTP method + pad-segment
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // [FromBody] = JSON body → LoginRequest object
        // IActionResult = Ok(...), Unauthorized(...), NotFound(), ...
        return Ok(new { ok = true });
    }
}
```

Belangrijke onderdelen:

| Onderdeel | Betekenis |
|---|---|
| `using …` | Imports (≈ PHP `use`) |
| `namespace …` | Organisatie van types |
| `[Something]` | **Attribute** — metadata voor routing/auth (≈ middleware/annotations) |
| `public class X : ControllerBase` | Class erft van base controller |
| `private readonly …` | Alleen lezen na constructor; DI-services |
| `async Task<…>` | Async methode (≈ Laravel awaitable) |
| `[FromBody]` | Bind JSON body aan parameter |
| `Ok` / `Unauthorized` / `BadRequest` | HTTP status + body |

### 2. Model (database-tabel)

```csharp
public class User
{
    public int Id { get; set; }           // primary key (conventie)
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public List<Session> Sessions { get; set; } = [];  // hasMany
}
```

| Onderdeel | Betekenis |
|---|---|
| `get; set;` | Property (geen public field) — EF mapt dit naar een kolom |
| `= string.Empty` | Default waarde (nullable-warnings voorkomen) |
| Navigatie (`Sessions`) | Relatie naar andere tabel |

### 3. DTO (alleen wat de API in/uit laat)

```csharp
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

Waarom apart van `User`? Zodat je nooit per ongeluk `PasswordHash` terugstuurt.

### 4. `AppDbContext` (EF Core)

```csharp
public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();       // tabel Users
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // indexes, uniques, foreign keys, …
    }
}
```

Gebruik in controller: `_db.Users.Where(…).ToListAsync()` ≈ Eloquent query.

### 5. `Program.cs` (startup)

Hier gebeurt bootstrapping:

1. **Services registreren** (`builder.Services.Add…`) → Dependency Injection  
2. **App bouwen** (`var app = builder.Build()`)  
3. **Middleware** (`UseAuthentication`, `UseAuthorization`)  
4. **Endpoints** (`MapControllers`)  

Zonder `AddScoped<TokenService>()` kan de controller `TokenService` niet injecteren.

### 6. Service

Gewone C#-class met logica (JWT maken, tokens hashen). Geen HTTP-kennis. Controllers blijven dunner en leesbaarder.

---

## Auth-flow (kort)

```
Client
  → Authorization: Bearer <access JWT>
  → UseAuthentication   (JWT valideren → ClaimsPrincipal op HttpContext.User)
  → UseAuthorization    ([Authorize] checken)
  → Controller          (user-id uit claims lezen)
  → EF Core             (LINQ → SQL)
  → PostgreSQL
```

- **Access token (JWT):** 15 min, zit niet in de database, niet blacklisten.  
- **Refresh token:** random string naar de client; in Postgres staat alleen de **hash** (`Sessions`).  
- **Logout / revoke:** zet `RevokedAt` op de session. Oude refresh token werkt daarna niet meer.

---

## Voorbeeld requests

Base URL: `http://localhost:5165`

```bash
# Register
curl -s http://localhost:5165/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Milan","lastName":"Nuis","email":"milan@example.com","password":"secret123"}'

# Login
curl -s http://localhost:5165/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"milan@example.com","password":"secret123"}'

# Me (vervang ACCESS_TOKEN)
curl -s http://localhost:5165/api/auth/me \
  -H "Authorization: Bearer ACCESS_TOKEN"

# Refresh
curl -s http://localhost:5165/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"REFRESH_TOKEN"}'

# Sessions
curl -s http://localhost:5165/api/auth/sessions \
  -H "Authorization: Bearer ACCESS_TOKEN"

# Session intrekken
curl -s -X DELETE http://localhost:5165/api/auth/sessions/1 \
  -H "Authorization: Bearer ACCESS_TOKEN"

# Logout
curl -s -X POST http://localhost:5165/api/auth/logout \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"REFRESH_TOKEN"}'
```

---

## Config

`appsettings.json`:

- `ConnectionStrings:DefaultConnection` — PostgreSQL  
- `Jwt:Key` / `Issuer` / `Audience` — JWT signing & validatie  

Op macOS met Homebrew Postgres is de user vaak je macOS-username (niet `postgres`).

---

## Tip: model wijzigen

1. Pas `Models/…` of `AppDbContext` aan  
2. `dotnet ef migrations add IetsDuidelijks`  
3. `dotnet ef database update`  
4. API herstarten  

Klaar.
