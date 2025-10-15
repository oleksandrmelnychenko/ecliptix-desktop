using DeadCodeHunter.Core;

namespace DeadCodeHunter.Util;

internal sealed class ConsoleProgressReporter
{
    private readonly Verbosity _verbosity;
    private readonly object _gate = new();

    public ConsoleProgressReporter(Verbosity verbosity)
    {
        _verbosity = verbosity;
    }

    public void WriteInfo(string message)
    {
        if (_verbosity == Verbosity.Quiet)
            return;

        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public void WriteDetailed(string message)
    {
        if (_verbosity < Verbosity.Detailed)
            return;

        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public void WriteWarning(string message)
    {
        if (_verbosity < Verbosity.Minimal)
            return;

        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public void WriteError(string message)
    {
        lock (_gate)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }
    }
}
