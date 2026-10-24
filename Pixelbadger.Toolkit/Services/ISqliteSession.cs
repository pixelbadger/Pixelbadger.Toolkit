namespace Pixelbadger.Toolkit.Services;

public enum SchemaObjectKind { Table, View }

public sealed record SchemaColumn(string Name, string Type, bool IsPrimaryKey, bool IsNotNull);

public sealed record SchemaObject(string Name, SchemaObjectKind Kind, IReadOnlyList<SchemaColumn> Columns);

public enum QueryOutcome { ResultSet, RowsAffected, NothingToExecute, Refused, Error }

public sealed record QueryResult(
    QueryOutcome Outcome,
    IReadOnlyList<string> Columns,                // empty unless Outcome == ResultSet
    IReadOnlyList<IReadOnlyList<object?>> Rows,   // empty unless ResultSet; cell values: null (SQL NULL), long, double, string, byte[]
    bool Truncated,                               // ResultSet only: more than SqliteSession.MaxRows rows existed
    int RowsAffected,                             // RowsAffected only: sum of rows changed by the batch's statements (>= 0)
    TimeSpan Elapsed,
    string? Message);                             // Error: the SQLite exception message (e.g. "SQLite Error 5: 'database is locked'.");
                                                  // Refused: "Transactions are managed by the TUI: Ctrl+S saves, F8 discards";
                                                  // NothingToExecute: "Nothing to execute"; otherwise null

public sealed record SessionActionResult(bool Success, string Message);

public interface ISqliteSession : IDisposable
{
    string DatabasePath { get; }            // full path as opened
    bool IsReadOnly { get; }
    bool HasUnsavedChanges { get; }         // "dirty": an open transaction holds uncommitted writes
    IReadOnlyList<SchemaObject> GetSchema(); // tables (excluding names starting "sqlite_") then views; each group ordered by name, case-insensitive; columns in declared order
    QueryResult Execute(string sql);
    SessionActionResult Save();             // dirty: COMMIT -> (true,"Changes saved"); commit failure -> (false, exception message), still dirty; clean -> (true,"No changes to save")
    SessionActionResult Discard();          // dirty: ROLLBACK -> (true,"Changes discarded"); clean -> (true,"No changes to discard"); failure -> (false, message)
}
