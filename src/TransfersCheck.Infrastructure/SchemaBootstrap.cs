using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;

namespace TransfersCheck.Infrastructure;

/// <summary>
/// Brings an existing database up to the current EF model without migrations (the repo has none).
/// <c>EnsureCreated</c> does nothing once any table exists, so a database created by an older build keeps
/// missing the tables and columns added since (for example <c>LoginAccounts.PasswordSet</c>).
/// This only ever adds: missing tables (with their seed rows), missing columns and missing indexes.
/// It never drops, renames or alters anything that already exists, and it is idempotent.
/// </summary>
public static class SchemaBootstrap
{
    /// <summary>Values for NOT NULL columns added to tables that already have rows. Anything else gets the CLR default.</summary>
    private static readonly Dictionary<(string Table, string Column), object> ExistingRowDefaults = new()
    {
        // Rows that predate the column are accounts that already have a working local password.
        [("LoginAccounts", "PasswordSet")] = true
    };

    public static async Task<IReadOnlyList<string>> EnsureAsync(TransfersCheckDb db, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        // Fresh database: EnsureCreated builds everything. Existing database: it is a no-op and the diff below fills the gaps.
        await db.Database.EnsureCreatedAsync(cancellationToken);

        var applied = new List<string>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(cancellationToken);
        try
        {
            var tables = await ReadSetAsync(connection,
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'", cancellationToken);
            var columns = await ReadSetAsync(connection,
                "SELECT TABLE_NAME + '.' + COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS", cancellationToken);
            var indexes = await ReadSetAsync(connection,
                "SELECT t.name + '.' + i.name FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.name IS NOT NULL",
                cancellationToken);

            var target = db.GetService<IDesignTimeModel>().Model;
            var differ = db.GetService<IMigrationsModelDiffer>();
            var generator = db.GetService<IMigrationsSqlGenerator>();
            var wanted = differ.GetDifferences(null, target.GetRelationalModel());

            var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tableOps = new List<MigrationOperation>();
            var columnOps = new List<MigrationOperation>();
            var indexOps = new List<(MigrationOperation Op, string Name)>();
            var seedOps = new List<InsertDataOperation>();

            foreach (var operation in wanted)
            {
                switch (operation)
                {
                    case CreateTableOperation create when !tables.Contains(create.Name):
                        createdTables.Add(create.Name);
                        tableOps.Add(create);
                        applied.Add($"table {create.Name}");
                        break;
                    case CreateTableOperation create:
                        foreach (var column in create.Columns.Where(c => !columns.Contains($"{create.Name}.{c.Name}")))
                        {
                            columnOps.Add(ToAddColumn(create.Name, column));
                            applied.Add($"column {create.Name}.{column.Name}");
                        }
                        break;
                    case CreateIndexOperation index when !indexes.Contains($"{index.Table}.{index.Name}"):
                        indexOps.Add((index, $"{index.Table}.{index.Name}"));
                        break;
                    case InsertDataOperation seed:
                        seedOps.Add(seed);
                        break;
                }
            }

            // Seed rows only for tables this run created; rows in existing tables belong to whoever put them there.
            var newSeeds = seedOps.Where(s => createdTables.Contains(s.Table)).Cast<MigrationOperation>();
            var ordered = tableOps.Concat(columnOps).Concat(newSeeds).ToList();
            if (ordered.Count > 0)
                await ExecuteAsync(connection, generator.Generate(ordered, target, MigrationsSqlGenerationOptions.Default), cancellationToken);

            foreach (var (op, name) in indexOps)
            {
                // A unique index can fail on legacy duplicates; that must not keep the site from starting.
                try
                {
                    await ExecuteAsync(connection, generator.Generate([op], target, MigrationsSqlGenerationOptions.Default), cancellationToken);
                    applied.Add($"index {name}");
                }
                catch (DbException exception)
                {
                    logger?.LogError(exception, "Schema bootstrap could not create index {Index}; continuing without it.", name);
                }
            }
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }

        if (applied.Count > 0)
            logger?.LogWarning("Schema bootstrap aligned the database with the model: {Changes}", string.Join(", ", applied));
        return applied;
    }

    private static AddColumnOperation ToAddColumn(string table, AddColumnOperation source)
    {
        var column = new AddColumnOperation
        {
            Table = table,
            Name = source.Name,
            ClrType = source.ClrType,
            ColumnType = source.ColumnType,
            IsUnicode = source.IsUnicode,
            IsFixedLength = source.IsFixedLength,
            MaxLength = source.MaxLength,
            Precision = source.Precision,
            Scale = source.Scale,
            IsRowVersion = source.IsRowVersion,
            IsNullable = source.IsNullable,
            DefaultValue = source.DefaultValue,
            DefaultValueSql = source.DefaultValueSql,
            ComputedColumnSql = source.ComputedColumnSql,
            IsStored = source.IsStored,
            Collation = source.Collation
        };
        if (!column.IsNullable && column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null)
        {
            column.DefaultValue = ExistingRowDefaults.TryGetValue((table, column.Name), out var chosen)
                ? chosen
                : DefaultFor(Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType);
        }
        return column;
    }

    private static object DefaultFor(Type type)
        => type == typeof(string) ? ""
            : type == typeof(byte[]) ? Array.Empty<byte>()
            : Activator.CreateInstance(type)!;

    private static async Task<HashSet<string>> ReadSetAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            set.Add(reader.GetString(0));
        return set;
    }

    private static async Task ExecuteAsync(DbConnection connection, IReadOnlyList<MigrationCommand> commands, CancellationToken cancellationToken)
    {
        foreach (var migration in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = migration.CommandText;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
