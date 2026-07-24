using Keepr.Api.Data;
using Keepr.Api.Features.Auth;
using Keepr.Api.OpenApi;
using Keepr.Api.Services;
using Keepr.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration bindings ------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<AuthSessionOptions>(builder.Configuration.GetSection(AuthSessionOptions.SectionName));
builder.Services.Configure<QuotaOptions>(builder.Configuration.GetSection(QuotaOptions.SectionName));
builder.Services.Configure<CleanupOptions>(builder.Configuration.GetSection(CleanupOptions.SectionName));
builder.Services.Configure<RegistrationOptions>(builder.Configuration.GetSection(RegistrationOptions.SectionName));

// ---- Persistence -----------------------------------------------------------
// Resolve from ConnectionStrings:Postgres (key-value or postgres:// URI) or discrete Db:* fields.
var pgConnection = PostgresConnectionString.Resolve(builder.Configuration);
if (string.IsNullOrWhiteSpace(pgConnection))
    throw new InvalidOperationException(
        "No Postgres connection configured. Set ConnectionStrings__Postgres, or the discrete " +
        "Db__Host / Db__Port / Db__Name / Db__Username / Db__Password env vars.");
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(pgConnection, npg =>
        // Keep the migrations-history table in our schema too, not "public".
        npg.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));

// ---- Storage + services ----------------------------------------------------
// Fail fast with a clear message if R2 credentials are missing (otherwise the AWS SDK throws a
// cryptic null-credential error on the first request that touches storage).
var storageCfg = builder.Configuration.GetSection(StorageOptions.SectionName);
if (string.IsNullOrWhiteSpace(storageCfg["AccessKey"]) || string.IsNullOrWhiteSpace(storageCfg["SecretKey"]))
    throw new InvalidOperationException(
        "Object storage credentials are missing. Set Storage__AccessKey and Storage__SecretKey " +
        "(and Storage__AccountId for R2, or Storage__ServiceUrl for a custom S3 endpoint).");
if (string.IsNullOrWhiteSpace(storageCfg["AccountId"]) && string.IsNullOrWhiteSpace(storageCfg["ServiceUrl"]))
    throw new InvalidOperationException(
        "Object storage endpoint is missing. Set Storage__AccountId (R2) or Storage__ServiceUrl (custom S3).");

builder.Services.AddSingleton<IObjectStorage, R2ObjectStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddSingleton<SessionCookie>();
// Who may create an account. Swap this line for another IRegistrationGate (emailed invites, an
// allow-list, an approval queue) without touching AuthController.
builder.Services.AddScoped<IRegistrationGate, InviteCodeRegistrationGate>();
builder.Services.AddScoped<FolderService>();
builder.Services.AddScoped<TrashService>();
builder.Services.AddHostedService<UploadCleanupService>();
builder.Services.AddHostedService<TrashPurgeService>();

// ---- Auth ------------------------------------------------------------------
// The session lives in an HttpOnly cookie holding an opaque id, not a JWT: a JWT stays valid
// until it expires no matter what the server thinks, so logout could not actually end a session.
// See docs/cookie-session-design.md.
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddKeeprOpenApi();

var app = builder.Build();

// Apply migrations on startup (fine for a single-instance monolith; gate for multi-instance).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Ensure our schema exists before the migrations-history table is created in it. The DB
    // owner can create a schema even when CREATE on "public" is denied (managed Postgres).
    db.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS \"{AppDbContext.Schema}\"");
    db.Database.Migrate();
}

// OpenAPI spec at /openapi/v1.json and Swagger UI at /swagger (Development only).
app.UseKeeprOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithSummary("Liveness probe.")
   .WithTags("Health")
   .AllowAnonymous();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ---- Serve the Angular SPA (built into wwwroot by the Dockerfile) ----------
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
