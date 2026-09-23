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
/// Creates a unique dedicated test database per test, applies all
/// EF Core migrations, and drops the database after the test.
/// Requires a local PostgreSQL server (same credentials as development).
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    private readonly string _testDatabaseName =
        "SFTecnologiasTestsDb_" + Guid.NewGuid().ToString("N")[..12];

    private string TestConnectionString =>
        "Host=localhost;Port=5432;Database=" + _testDatabaseName +
        ";Username=postgres;Password=postgres";

    public async Task InitializeAsync()
    {
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

    private async Task DropTestDatabaseIfExistsAsync()
    {
        NpgsqlConnection.ClearAllPools();

        Exception? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(MasterConnectionString);
                await connection.OpenAsync();
                await using var dropCommand = connection.CreateCommand();
                dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_testDatabaseName}\" WITH (FORCE)";
                await dropCommand.ExecuteNonQueryAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                NpgsqlConnection.ClearAllPools();
                await Task.Delay(150 * attempt);
            }
        }
        throw new InvalidOperationException(
            $"Failed to drop test database {_testDatabaseName}", last);
    }

    private async Task CreateTestDatabaseAsync()
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(MasterConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{_testDatabaseName}\"";
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException ex)
                when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
            {
                last = ex;
                await DropTestDatabaseIfExistsAsync();
                await Task.Delay(100 * attempt);
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(100 * attempt);
            }
        }
        throw new InvalidOperationException(
            $"Failed to create test database {_testDatabaseName}", last);
    }
}
