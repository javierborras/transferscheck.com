using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

namespace TransfersCheck.Tests;

/// <summary>
/// The repo has no migrations and EnsureCreated skips a database that already has tables, so an old database
/// lacked LoginAccounts.PasswordSet/UserName and whole tables (the "Invalid column name 'PasswordSet'" 500 on Auth0 sign-up).
/// </summary>
public sealed class SchemaBootstrapTests
{
    private const string Connection =
        "Server=localhost\\SQLEXPRESS;Database=TransfersCheckSchemaDrift;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    private static TransfersCheckDb NewDb()
    {
        var options = new DbContextOptionsBuilder<TransfersCheckDb>()
            .UseSqlServer(Connection, sql => sql.UseCompatibilityLevel(SqlCompat.Level))
            .Options;
        return new TransfersCheckDb(options, new TenantContext());
    }

    private static async Task<int> Scalar(string sql)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task Run(string sql)
    {
        await using var connection = new SqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task An_old_database_gets_the_missing_columns_tables_and_seed_rows_without_losing_data()
    {
        await using (var db = NewDb())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        // Recreate what an older build left behind: no PasswordSet/UserName, no later tables, no alias index.
        await Run("ALTER TABLE LoginAccounts DROP COLUMN UserName, PasswordSet");
        await Run("DROP TABLE ConsultaEvents; DROP TABLE Plans; DROP TABLE AlertInvites; DROP TABLE EmailSignInChallenges; DROP TABLE ReferralGrants; DROP TABLE OwnerSecrets");
        await Run("DROP INDEX IX_Merchants_Alias ON Merchants");
        var legacy = Guid.NewGuid();
        await Run($"INSERT INTO LoginAccounts (Id, TenantId, Email, PasswordHash, IsOwner, Revoked, PushApproved) VALUES ('{legacy}', '{legacy}', 'legacy@old.test', 'hash', 1, 0, 0)");

        await using (var db = NewDb())
        {
            var applied = await SchemaBootstrap.EnsureAsync(db);

            Assert.Contains("column LoginAccounts.PasswordSet", applied);
            Assert.Contains("column LoginAccounts.UserName", applied);
            Assert.Contains("table Plans", applied);
            Assert.Contains("table AlertInvites", applied);
            Assert.Contains("index Merchants.IX_Merchants_Alias", applied);
        }

        // Pre-existing accounts keep a working local password; the table that was recreated is seeded.
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM LoginAccounts WHERE Id = '{legacy}' AND PasswordSet = 1 AND UserName = ''"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM Plans"));

        // The model now reads and writes the account the way the Auth0 callback does.
        await using (var db = NewDb())
        {
            var tenant = Guid.NewGuid();
            db.LoginAccounts.Add(new LoginAccount { Id = Guid.NewGuid(), TenantId = tenant, Email = "new@auth0.test", PasswordHash = "x", IsOwner = true, PasswordSet = false });
            await db.SaveChangesAsync();
            Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM LoginAccounts WHERE Email = 'new@auth0.test' AND PasswordSet = 0"));
            Assert.True(await db.LoginAccounts.IgnoreQueryFilters().AnyAsync(a => a.Email == "legacy@old.test" && a.PasswordSet));
        }

        // Idempotent: a healthy database is left alone.
        await using (var db = NewDb())
            Assert.Empty(await SchemaBootstrap.EnsureAsync(db));
    }

    [Fact]
    public async Task A_database_that_only_has_foreign_tables_is_still_completed()
    {
        // Hangfire can create its tables before the app does; EnsureCreated would then skip every app table.
        await using (var db = NewDb())
        {
            await db.Database.EnsureDeletedAsync();
            // An empty database whose only table is not ours.
            await db.Database.EnsureCreatedAsync();
        }
        await Run("DROP TABLE ConsultaEvents; DROP TABLE Plans; DROP TABLE AlertInvites; DROP TABLE EmailSignInChallenges; DROP TABLE ReferralGrants; DROP TABLE OwnerSecrets;"
            + " DROP TABLE ProcessedPayments; DROP TABLE PushDevices; DROP TABLE Transfers; DROP TABLE TransferTypes; DROP TABLE Connections;"
            + " DROP TABLE OAuthAttempts; DROP TABLE LoginSessions; DROP TABLE LoginAccounts; DROP TABLE Merchants");
        await Run("CREATE TABLE ForeignThing (Id int NOT NULL)");

        await using (var db = NewDb())
        {
            var applied = await SchemaBootstrap.EnsureAsync(db);
            Assert.Contains("table LoginAccounts", applied);
            Assert.Contains("table Merchants", applied);
        }

        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM Plans"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM TransferTypes"));
    }
}
