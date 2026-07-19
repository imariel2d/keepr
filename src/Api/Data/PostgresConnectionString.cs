using Npgsql;

namespace Keepr.Api.Data;

/// <summary>
/// Normalizes a Postgres connection string. Accepts either the standard Npgsql key-value form
/// (<c>Host=...;Port=...;...</c>) or a URI (<c>postgresql://user:pass@host:port/db?sslmode=...</c>)
/// such as DigitalOcean's <c>DATABASE_URL</c>, and always returns the key-value form Npgsql expects.
/// SSL is required for managed Postgres (DO), so it is enforced on URI inputs.
/// </summary>
public static class PostgresConnectionString
{
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
            // Npgsql 8+: Require encrypts without validating the server cert, which is what
            // DO Managed Postgres needs (no CA bundle to ship).
            SslMode = SslMode.Require,
        };
        return builder.ConnectionString;
    }
}
