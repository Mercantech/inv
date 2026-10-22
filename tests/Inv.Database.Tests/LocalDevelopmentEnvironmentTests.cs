using Inv.Infrastructure.Configuration;
using Npgsql;
using Xunit;

namespace Inv.Database.Tests;

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

[Collection("Process environment")]
public sealed class LocalDevelopmentEnvironmentTests
{
    [Fact]
    public void Development_loads_local_database_settings_without_overwriting_existing_environment()
    {
        using var environment = new LocalEnvironmentScope();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("AUTH_ISSUER", "https://existing.example");

        LocalDevelopmentEnvironment.Load();

        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
        Assert.Equal("127.0.0.1", connection.Host);
        Assert.Equal(5544, connection.Port);
        Assert.Equal("local_inventory", connection.Database);
        Assert.Equal("test;password", connection.Password);
        Assert.Equal("https://existing.example", Environment.GetEnvironmentVariable("AUTH_ISSUER"));
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Development", "true")]
    public void Production_and_containers_ignore_the_local_env_file(string name, string? container)
    {
        using var environment = new LocalEnvironmentScope();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", name);
        Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", container);

        LocalDevelopmentEnvironment.Load();

        Assert.Null(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
        Assert.Null(Environment.GetEnvironmentVariable("AUTH_ISSUER"));
    }

    [Fact]
    public void Explicit_connection_is_preserved()
    {
        using var environment = new LocalEnvironmentScope();
        const string configuredConnection = "Host=external.example;Database=existing";
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", configuredConnection);

        LocalDevelopmentEnvironment.Load();

        Assert.Null(Environment.GetEnvironmentVariable("CONNECTION_STRING"));
        Assert.Equal(configuredConnection, Environment.GetEnvironmentVariable("ConnectionStrings__Default"));
    }

    private sealed class LocalEnvironmentScope : IDisposable
    {
        private static readonly string[] Keys =
        [
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "DOTNET_RUNNING_IN_CONTAINER",
            "CONNECTION_STRING", "ConnectionStrings__Default", "POSTGRES_PORT", "POSTGRES_DB",
            "POSTGRES_USER", "POSTGRES_PASSWORD", "AUTH_ISSUER"
        ];
        private readonly string previousDirectory = Directory.GetCurrentDirectory();
        private readonly Dictionary<string, string?> previousVariables = Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        private readonly string path = Path.Combine(Path.GetTempPath(), $"inv-config-test-{Guid.NewGuid():N}");

        public LocalEnvironmentScope()
        {
            foreach (var key in Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "Inv.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(path, ".env"), """
                POSTGRES_PORT=5544
                POSTGRES_DB=local_inventory
                POSTGRES_USER=local_user
                POSTGRES_PASSWORD='test;password'
                AUTH_ISSUER=https://file.example
                """);
            Directory.SetCurrentDirectory(path);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            foreach (var (key, value) in previousVariables)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            File.Delete(Path.Combine(path, ".env"));
            File.Delete(Path.Combine(path, "Inv.slnx"));
            Directory.Delete(path);
        }
    }
}
