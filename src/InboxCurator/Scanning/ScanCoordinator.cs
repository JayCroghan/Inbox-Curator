using System.Threading.Channels;
using InboxCurator.Gmail;
using Microsoft.Extensions.Options;

namespace InboxCurator.Scanning;

public interface IScanTrigger
{
    bool RequestScan();
    bool IsRunning { get; }
}

public sealed class ScanCoordinator(
    MailboxScanner scanner,
    IOptions<GmailOptions> options,
    ILogger<ScanCoordinator> logger) : BackgroundService, IScanTrigger
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    public bool RequestScan() => _requests.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ScanOnStartup)
        {
            RequestScan();
        }

        await foreach (var _ in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Exchange(ref _isRunning, 1);
            try
            {
                await scanner.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError("Mailbox scan failed with {FailureType}. Use the dashboard to resume from the last checkpoint.", exception.GetType().Name);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }
    }
}
