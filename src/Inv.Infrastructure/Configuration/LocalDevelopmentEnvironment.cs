using DotNetEnv;
using Npgsql;

namespace Inv.Infrastructure.Configuration;

public static class LocalDevelopmentEnvironment
{
    public static void Load()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inv.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return;
        }

        var path = Path.Combine(directory.FullName, ".env");
        if (File.Exists(path))
        {
            Env.NoClobber().Load(path);
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONNECTION_STRING"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Default")))
        {
            return;
        }

        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        if (!int.TryParse(port, out var databasePort) || databasePort is < 1 or > 65535)
        {
            throw new InvalidOperationException("POSTGRES_PORT skal være et portnummer mellem 1 og 65535.");
        }

        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = databasePort,
            Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "inv",
            Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "inv",
            Password = password
        };

        Environment.SetEnvironmentVariable("CONNECTION_STRING", connection.ConnectionString);
    }
}
