using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TransfersCheck.Core;
using TransfersCheck.Infrastructure;

// Isolated SQL setup: no web host, API, timers, payments, or mock-server reference.
if (args.Length != 4 || args[3] != "--initialize-empty") {
    Console.Error.WriteLine("Usage: DatabaseSetup <private-settings.json> <public-sql-host> <expected-database> --initialize-empty");
    return 2;
}
try {
    using var config = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    var connection = new SqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("TransfersCheck").GetString());
    if (connection.InitialCatalog != args[2]) throw new InvalidOperationException("The configured database does not match the explicit target.");
    connection.DataSource = "tcp:" + args[1] + ",1433";
    connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
    connection.TrustServerCertificate = false;
    connection.ApplicationName = "TransfersCheck.IsolatedSchemaSetup";
    await using var sql = new SqlConnection(connection.ConnectionString);
    await sql.OpenAsync();
    await using var check = sql.CreateCommand();
    check.CommandText = "SELECT DB_NAME(); SELECT COUNT(*) FROM sys.tables;";
    await using (var reader = await check.ExecuteReaderAsync()) {
        await reader.ReadAsync();
        if (reader.GetString(0) != args[2]) throw new InvalidOperationException("The connected database does not match the explicit target.");
        await reader.NextResultAsync(); await reader.ReadAsync();
        if (reader.GetInt32(0) != 0) throw new InvalidOperationException("Database is not empty. This initializer refuses to modify existing schemas or data.");
    }
    var options = new DbContextOptionsBuilder<TransfersCheckDb>().UseSqlServer(connection.ConnectionString, sqlOptions => sqlOptions.UseCompatibilityLevel(SqlCompat.Level)).Options;
    await using var db = new TransfersCheckDb(options, new TenantContext());
    await SchemaBootstrap.EnsureAsync(db);
    Console.WriteLine("Initialized the explicitly selected empty database with the TransfersCheck schema and catalog seeds. No API or background services started.");
    return 0;
} catch (Exception error) {
    // Provider errors may contain connection details; do not print those or stack traces.
    Console.Error.WriteLine("Database initialization failed (" + error.GetType().Name + "). No web services were started. Inspect the target state before retrying.");
    return 1;
}
