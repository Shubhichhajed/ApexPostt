using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ApexPost.IntegrationTests.Authentication;

public sealed class ApexPostWebFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName;
    private readonly string _testConnectionString;
    private readonly string _allowedIp;
    private readonly bool _maintenanceEnabled;
    private readonly string? _apiKey;
    private readonly string? _previousConnectionStringEnvironmentValue;
    private bool _connectionStringEnvironmentOverridden;
    private bool _initialized;

    public ApexPostWebFactory(string allowedIp = "127.0.0.1", bool maintenanceEnabled = false, string? apiKey = "integration-test-secret")
    {
        _databaseName = $"ApexPost_Test_{Guid.NewGuid():N}";
        _testConnectionString = BuildTestConnectionString(_databaseName);
        _allowedIp = allowedIp;
        _maintenanceEnabled = maintenanceEnabled;
        _apiKey = apiKey;

        // WebApplication.CreateBuilder(args) reads environment configuration while
        // Program.cs is executing. Set this before WebApplicationFactory starts the
        // application so the connection string exists at the first configuration read.
        _previousConnectionStringEnvironmentValue =
            Environment.GetEnvironmentVariable("ConnectionStrings__ApexPost");

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__ApexPost",
            _testConnectionString);

        _connectionStringEnvironmentOverridden = true;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        var master = new SqlConnectionStringBuilder(_testConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{_databaseName}') IS NULL CREATE DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
        _initialized = true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ApexPost"] = _testConnectionString,
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["DemoPasswords:CounterClerk"] = "CounterTestPassword!123",
            ["DemoPasswords:BranchManager"] = "ManagerTestPassword!123",
            ["DemoPasswords:Auditor"] = "AuditorTestPassword!123",
            ["DemoPasswords:Administrator"] = "AdminTestPassword!123",
            ["Integration:ApiKey"] = _apiKey,
            ["Security:AllowedIps:0"] = _allowedIp,
            // TestServer may report IPv4 or IPv6 loopback. Permit both only for
            // the default local test factory; explicit blocked-IP tests remain blocked.
            ["Security:AllowedIps:1"] = _allowedIp is "127.0.0.1" or "::1" ? "127.0.0.1" : null,
            ["Security:AllowedIps:2"] = _allowedIp is "127.0.0.1" or "::1" ? "::1" : null,
            ["Maintenance:Enabled"] = _maintenanceEnabled.ToString()
        }));
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            try
            {
                if (_initialized)
                {
                    var master = new SqlConnectionStringBuilder(_testConnectionString)
                    {
                        InitialCatalog = "master"
                    };

                    await using var connection =
                        new SqlConnection(master.ConnectionString);

                    try
                    {
                        await connection.OpenAsync();
                        await using var command = connection.CreateCommand();
                        command.CommandText =
                            $"IF DB_ID(N'{_databaseName}') IS NOT NULL " +
                            $"BEGIN ALTER DATABASE [{_databaseName}] " +
                            "SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                            $"DROP DATABASE [{_databaseName}]; END";

                        await command.ExecuteNonQueryAsync();
                    }
                    catch (SqlException)
                    {
                        // Cleanup must not hide the original test failure.
                    }
                }
            }
            finally
            {
                if (_connectionStringEnvironmentOverridden)
                {
                    Environment.SetEnvironmentVariable(
                        "ConnectionStrings__ApexPost",
                        _previousConnectionStringEnvironmentValue);

                    _connectionStringEnvironmentOverridden = false;
                }
            }
        }
    }

    private static string BuildTestConnectionString(string databaseName)
    {
        var configured = Environment.GetEnvironmentVariable("APEXPOST_TEST_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };
        return builder.ConnectionString;
    }
}
