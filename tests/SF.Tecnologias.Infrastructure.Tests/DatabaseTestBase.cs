using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SF.Tecnologias.Infrastructure.Tests;

/// <summary>
/// Base class for database integration tests.
/// Creates a dedicated test database (SFTecnologiasTestsDb), applies all
/// EF Core migrations, and drops the database after the test run.
/// Requires a local PostgreSQL server (same credentials as development).
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    private const string TestDatabaseName = "SFTecnologiasTestsDb";
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=" + TestDatabaseName + ";Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
        await DropTestDatabaseIfExistsAsync();
        await CreateTestDatabaseAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await DropTestDatabaseIfExistsAsync();
    }

    protected AppDbContext CreateContext(ITenantProvider? tenantProvider = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestConnectionString)
            .Options;
        return new AppDbContext(options, tenantProvider);
    }

    protected async Task<System.Collections.Generic.List<string>> GetTableNamesAsync()
    {
        await using var connection = new NpgsqlConnection(TestConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";
        var names = new System.Collections.Generic.List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private static async Task DropTestDatabaseIfExistsAsync()
    {
        // Clear pooled connections so DROP DATABASE does not kill connections
        // that later tests would otherwise reuse (causing "connection forcibly closed").
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var killCommand = connection.CreateCommand();
        killCommand.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1 AND pid <> pg_backend_pid()";
        killCommand.Parameters.AddWithValue(TestDatabaseName);
        await killCommand.ExecuteNonQueryAsync();
        await using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{TestDatabaseName}\"";
        await dropCommand.ExecuteNonQueryAsync();
    }

    private static async Task CreateTestDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{TestDatabaseName}\"";
        await command.ExecuteNonQueryAsync();
    }
}
