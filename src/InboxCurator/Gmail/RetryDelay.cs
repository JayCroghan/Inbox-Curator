namespace InboxCurator.Gmail;

public interface IRetryDelay
{
    TimeSpan GetDelay(int attempt);
    Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemRetryDelay : IRetryDelay
{
    public TimeSpan GetDelay(int attempt)
    {
        var exponentialSeconds = Math.Min(60, Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(exponentialSeconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(50, 251));
    }

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
