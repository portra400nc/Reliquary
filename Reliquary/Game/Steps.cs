namespace Reliquary;

// Runs one named game call. A failure is logged the first time its step fails and the caller gets the fallback, so a
// broken call cannot flood the log or stop the calls after it.
internal sealed class Steps(Action<string> log)
{
    private readonly HashSet<string> reported = [];

    public T Try<T>(string step, Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception error)
        {
            if (reported.Add(step))
                log($"step '{step}' failed: {error}");
            return fallback;
        }
    }

    public bool Run(string step, Action call) => Try(step, () => { call(); return true; }, false);
}
