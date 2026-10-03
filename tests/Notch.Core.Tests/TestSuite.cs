namespace Notch.Core.Tests;

internal sealed class TestSuite
{
    private readonly List<(string Name, Func<Task> Run)> _cases = [];

    public void Add(string name, Action run) => _cases.Add((name, () =>
    {
        run();
        return Task.CompletedTask;
    }));

    public void AddAsync(string name, Func<Task> run) => _cases.Add((name, run));

    public async Task<int> RunAsync()
    {
        if (_cases.Count == 0)
        {
            Console.Error.WriteLine("ERROR: no tests were registered.");
            return 1;
        }
        var failed = 0;
        foreach (var test in _cases)
        {
            try
            {
                await test.Run().WaitAsync(TimeSpan.FromSeconds(30));
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}: {error.Message}");
            }
        }
        Console.WriteLine($"{_cases.Count} tests executed, {_cases.Count - failed} passed, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}

internal static class Check
{
    public static void True(bool condition, string message = "Expected true")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message = "Expected false") => True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}");
    }

    public static void Near(double expected, double actual, double tolerance = 0.00001)
    {
        if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected {expected} ± {tolerance}; got {actual}");
    }

    public static TException Throws<TException>(Action run) where TException : Exception
    {
        try { run(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> run) where TException : Exception
    {
        try { await run(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }
}
