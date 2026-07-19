using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Keepr.Api.Data;

/// <summary>
/// Resolves the Postgres connection string from configuration, supporting three shapes so it
/// works across environments:
///   1. <c>ConnectionStrings:Postgres</c> as an Npgsql key-value string, or
///   2. <c>ConnectionStrings:Postgres</c> as a URI (<c>postgresql://user:pass@host:port/db</c>,
///      e.g. DigitalOcean's DATABASE_URL), or
///   3. discrete <c>Db:*</c> fields (Host/Port/Name/Username/Password/SslMode) — handy when you'd
///      rather set separate env vars (Db__Host, Db__Port, ...).
/// SSL defaults to Require, which managed Postgres (DO) needs.
/// </summary>
public static class PostgresConnectionString
{
    /// <summary>Pick the best-available source and return an Npgsql key-value connection string.</summary>
    public static string? Resolve(IConfiguration config)
    {
        var direct = config.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(direct))
            return Normalize(direct);

        // Fall back to discrete Db:* fields.
        var db = config.GetSection("Db");
        var host = db["Host"];
        if (string.IsNullOrWhiteSpace(host))
            return null; // nothing configured; let the caller surface a clear error

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(db["Port"], out var port) ? port : 5432,
            Database = db["Name"],
            Username = db["Username"],
            Password = db["Password"],
            SslMode = ParseSslMode(db["SslMode"]),
        };
        return builder.ConnectionString;
    }

    /// <summary>Accept a key-value string as-is, or convert a postgres:// URI to key-value form.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var isUri = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);
        if (!isUri) return value; // already key-value form

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = ParseSslMode(GetQueryValue(uri.Query, "sslmode")),
        };
        return builder.ConnectionString;
    }

    // Npgsql 8+: Require encrypts without validating the server cert — what DO Managed Postgres
    // needs (no CA bundle to ship). Unknown/empty defaults to Require.
    private static SslMode ParseSslMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => SslMode.Require,
    };

    private static string? GetQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return null;
    }
}
