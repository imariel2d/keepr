using System.Text;
using Keepr.Api.Data;
using Keepr.Api.Features.Auth;
using Keepr.Api.Services;
using Keepr.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration bindings ------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<QuotaOptions>(builder.Configuration.GetSection(QuotaOptions.SectionName));
builder.Services.Configure<CleanupOptions>(builder.Configuration.GetSection(CleanupOptions.SectionName));

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
        npg.MigrationsHistoryTable("__EFMigrationsHistory", "keepr")));

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
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddHostedService<UploadCleanupService>();

// ---- Auth ------------------------------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Apply migrations on startup (fine for a single-instance monolith; gate for multi-instance).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Ensure our schema exists before the migrations-history table is created in it. The DB
    // owner can create a schema even when CREATE on "public" is denied (managed Postgres).
    db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS \"keepr\"");
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ---- Serve the Angular SPA (built into wwwroot by the Dockerfile) ----------
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
