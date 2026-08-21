using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CCTavern.Database;

/// <summary>
/// MySQL-compatible migration history repository with MariaDB-safe lock result handling.
/// </summary>
internal sealed class MariaDbHistoryRepository : HistoryRepository {
    private const string MigrationLockName = "__EFMigrationsLock";
    private const int MigrationLockTimeoutSeconds = 15;

    public MariaDbHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies) { }

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Connection;

    public override IMigrationsDatabaseLock AcquireDatabaseLock() {
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        var databaseLock = CreateMigrationDatabaseLock();
        try {
            var result = CreateGetLockCommand()
                .ExecuteScalar(CreateRelationalCommandParameters());

            EnsureLockAcquired(result);
            return databaseLock;
        } catch {
            try {
                databaseLock.Dispose();
            } catch {
                // Preserve the lock acquisition failure.
            }
            throw;
        }
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default
    ) {
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        var databaseLock = CreateMigrationDatabaseLock();
        try {
            var result = await CreateGetLockCommand()
                .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false);

            EnsureLockAcquired(result);
            return databaseLock;
        } catch {
            try {
                await databaseLock.DisposeAsync().ConfigureAwait(false);
            } catch {
                // Preserve the lock acquisition failure.
            }
            throw;
        }
    }

    protected override string ExistsSql {
        get {
            var stringMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
            var schema = TableSchema ?? Dependencies.Connection.DbConnection.Database;

            return new StringBuilder()
                .Append("SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=")
                .Append(stringMapping.GenerateSqlLiteral(schema))
                .Append(" AND TABLE_NAME=")
                .Append(stringMapping.GenerateSqlLiteral(TableName))
                .Append(';')
                .ToString();
        }
    }

    protected override bool InterpretExistsResult(object? value) => value != null;

    public override string GetBeginIfExistsScript(string migrationId)
        => GetBeginConditionalScript(migrationId, "IF EXISTS");

    public override string GetBeginIfNotExistsScript(string migrationId)
        => GetBeginConditionalScript(migrationId, "IF NOT EXISTS");

    public override string GetCreateIfNotExistsScript() {
        var script = GetCreateScript();
        var createTableIndex = script.IndexOf("CREATE TABLE", StringComparison.Ordinal);
        return script.Insert(createTableIndex + "CREATE TABLE".Length, " IF NOT EXISTS");
    }

    public override string GetEndIfScript() => $"END;{Environment.NewLine}";

    private static void EnsureLockAcquired(object? result) {
        if (result is null || result is DBNull) {
            throw new InvalidOperationException("MariaDB returned no result while acquiring the EF migrations lock.");
        }

        long convertedResult;
        try {
            convertedResult = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        } catch (Exception exception) when (exception is FormatException
                or InvalidCastException
                or OverflowException) {
            throw new InvalidOperationException(
                $"MariaDB returned an unsupported EF migrations lock result type: {result.GetType().FullName}.",
                exception);
        }

        if (convertedResult != 1) {
            throw new TimeoutException("Unable to acquire the EF migrations database lock.");
        }
    }

    private string GetBeginConditionalScript(string migrationId, string condition) {
        ArgumentException.ThrowIfNullOrEmpty(migrationId);

        var stringMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
        return new StringBuilder()
            .Append(condition)
            .Append("(SELECT * FROM ")
            .Append(SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema))
            .Append(" WHERE ")
            .Append(SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName))
            .Append(" = ")
            .Append(stringMapping.GenerateSqlLiteral(migrationId))
            .AppendLine(")")
            .Append("BEGIN")
            .ToString();
    }

    private IRelationalCommand CreateGetLockCommand()
        => Dependencies.RawSqlCommandBuilder
            .Build($"SELECT GET_LOCK('{MigrationLockName}', {MigrationLockTimeoutSeconds});");

    private IMigrationsDatabaseLock CreateMigrationDatabaseLock()
        => new MariaDbMigrationDatabaseLock(
            Dependencies.RawSqlCommandBuilder
                .Build($"SELECT RELEASE_LOCK('{MigrationLockName}');"),
            CreateRelationalCommandParameters(),
            this);

    private RelationalCommandParameterObject CreateRelationalCommandParameters()
        => new(
            Dependencies.Connection,
            null,
            null,
            Dependencies.CurrentContext.Context,
            Dependencies.CommandLogger,
            CommandSource.Migrations);

    private sealed class MariaDbMigrationDatabaseLock(
        IRelationalCommand releaseLockCommand,
        RelationalCommandParameterObject commandParameters,
        IHistoryRepository historyRepository
    ) : IMigrationsDatabaseLock {
        public IHistoryRepository HistoryRepository { get; } = historyRepository;

        public void Dispose()
            => releaseLockCommand.ExecuteScalar(commandParameters);

        public async ValueTask DisposeAsync()
            => await releaseLockCommand
                .ExecuteScalarAsync(commandParameters, CancellationToken.None)
                .ConfigureAwait(false);
    }
}