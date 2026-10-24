using System.CommandLine;
using Pixelbadger.Toolkit.CommandLine;
using Pixelbadger.Toolkit.Components;
using Pixelbadger.Toolkit.Services;
using Spectre.Console;

namespace Pixelbadger.Toolkit.Commands;

public static class SqliteCommand
{
    public static Command Create() => Create(() => !Console.IsInputRedirected && !Console.IsOutputRedirected);

    /// <summary>Builds the topic with an injectable interactivity check so the non-terminal path is testable.</summary>
    internal static Command Create(Func<bool> isInteractive)
    {
        ArgumentNullException.ThrowIfNull(isInteractive);

        var command = new Command("sqlite", "Interactive SQLite tools").ExcludeFromMcp();

        command.Add(CreateTuiCommand(isInteractive));

        return command;
    }

    private static Command CreateTuiCommand(Func<bool> isInteractive)
    {
        var command = new Command("tui", "Interactive terminal UI for browsing and querying a SQLite database");

        var dbOption = new Option<string>("--db") { Description = "Path to an existing SQLite database file", Required = true };

        command.Add(dbOption);

        command.SetAction((parseResult, cancellationToken) =>
            Task.FromResult(RunTui(parseResult.GetValue(dbOption)!, isInteractive, cancellationToken)));

        return command;
    }

    private static int RunTui(string dbPath, Func<bool> isInteractive, CancellationToken cancellationToken)
    {
        try
        {
            ISqliteSession session;
            try
            {
                session = SqliteSession.Open(dbPath);
            }
            catch (SqliteSessionOpenException ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }

            if (!isInteractive())
            {
                session.Dispose();
                AnsiConsole.MarkupLine("[red]Error:[/] sqlite tui requires an interactive terminal");
                return 1;
            }

            using (session)
            {
                return new SqliteTuiComponent(session).Run(AnsiConsole.Console, new ConsoleTerminalInput(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
