using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Appostolic.Api.Infrastructure.Database;

public static class ConnectionStringHelper
{
    public static string BuildFromEnvironment(IConfiguration configuration)
    {
        var host = configuration["POSTGRES_HOST"] ?? "localhost";
        var port = configuration["POSTGRES_PORT"] ?? "55432";
        var db = configuration["POSTGRES_DB"] ?? "appdb";
        var user = configuration["POSTGRES_USER"] ?? "appuser";
        var pass = configuration["POSTGRES_PASSWORD"] ?? "apppassword";

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var p) ? p : 55432,
            Database = db,
            Username = user,
            Password = pass,
            SslMode = SslMode.Disable
        };
        return builder.ConnectionString;
    }
}
