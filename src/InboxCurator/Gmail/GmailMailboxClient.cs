using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;
using InboxCurator.Data;
using Microsoft.Extensions.Options;

namespace InboxCurator.Gmail;

public sealed class GmailMailboxClient(
    IGoogleCredentialProvider credentialProvider,
    IRetryDelay retryDelay,
    IOptions<GmailOptions> options,
    ILogger<GmailMailboxClient> logger) : IGmailMailboxClient, IDisposable
{
    private static readonly string[] SelectedHeaders =
    [
        "From", "Reply-To", "To", "Cc", "Bcc", "Subject", "Date", "List-ID", "List-Unsubscribe"
    ];
    internal const string AttachmentStructureFields =
        "payload(filename,body/attachmentId,parts(filename,body/attachmentId,parts(filename,body/attachmentId,parts(filename,body/attachmentId,parts(filename,body/attachmentId,parts(filename,body/attachmentId))))))";

    private readonly SemaphoreSlim _serviceLock = new(1, 1);
    private GmailService? _service;

    public async Task<GmailPage> ListAsync(ScanKind kind, string? pageToken, CancellationToken cancellationToken)
    {
        return await ExecuteWithBackoffAsync(async () =>
        {
            var service = await GetServiceAsync(cancellationToken);
            var request = service.Users.Messages.List("me");
            request.Q = kind == ScanKind.Sent
                ? "in:sent -in:trash -in:spam -in:drafts"
                : "-in:trash -in:spam -in:drafts -in:sent";
            request.IncludeSpamTrash = false;
            request.MaxResults = Math.Clamp(options.Value.PageSize, 1, 500);
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(cancellationToken);
            var ids = response.Messages?.Select(message => message.Id).Where(id => id is not null).ToArray() ?? [];
            return new GmailPage(ids!, response.NextPageToken);
        }, cancellationToken);
    }

    public async Task<GmailMessageMetadata> GetMetadataAsync(string messageId, CancellationToken cancellationToken)
    {
        return await ExecuteWithBackoffAsync(async () =>
        {
            var service = await GetServiceAsync(cancellationToken);
            var request = service.Users.Messages.Get("me", messageId);
            request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            request.MetadataHeaders = new Repeatable<string>(SelectedHeaders);
            var message = await request.ExecuteAsync(cancellationToken);

            // Gmail's METADATA format intentionally omits the MIME part tree. A second,
            // ordered partial response retrieves attachment structure without body data.
            var structureRequest = service.Users.Messages.Get("me", messageId);
            structureRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            structureRequest.Fields = AttachmentStructureFields;
            var structure = await structureRequest.ExecuteAsync(cancellationToken);
            return Map(message, HasAttachment(structure.Payload));
        }, cancellationToken);
    }

    private async Task<GmailService> GetServiceAsync(CancellationToken cancellationToken)
    {
        if (_service is not null)
        {
            return _service;
        }

        await _serviceLock.WaitAsync(cancellationToken);
        try
        {
            if (_service is null)
            {
                var credential = await credentialProvider.AuthorizeAsync(cancellationToken);
                _service = new GmailService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = options.Value.ApplicationName
                });
            }

            return _service;
        }
        finally
        {
            _serviceLock.Release();
        }
    }

    private async Task<T> ExecuteWithBackoffAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Clamp(options.Value.MaxRetryAttempts, 1, 10);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (GoogleApiException exception) when (IsTransient(exception) && attempt + 1 < maxAttempts)
            {
                var delay = retryDelay.GetDelay(attempt);
                logger.LogWarning("Gmail API throttled or temporarily unavailable; retrying attempt {Attempt} after {DelayMs} ms.", attempt + 2, delay.TotalMilliseconds);
                await retryDelay.WaitAsync(delay, cancellationToken);
            }
        }
    }

    internal static bool IsTransient(GoogleApiException exception)
    {
        var status = exception.HttpStatusCode;
        return status == HttpStatusCode.TooManyRequests ||
               status == HttpStatusCode.InternalServerError ||
               status == HttpStatusCode.BadGateway ||
               status == HttpStatusCode.ServiceUnavailable ||
               status == HttpStatusCode.GatewayTimeout;
    }

    private static GmailMessageMetadata Map(Message message, bool hasAttachment)
    {
        var headers = (message.Payload?.Headers ?? [])
            .Where(header => !string.IsNullOrWhiteSpace(header.Name))
            .GroupBy(header => header.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var timestamp = message.InternalDate ?? 0;
        var date = timestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime
            : DateTime.UnixEpoch;

        return new GmailMessageMetadata(
            message.Id ?? throw new InvalidDataException("Gmail returned metadata without a message ID."),
            message.ThreadId ?? string.Empty,
            date,
            message.LabelIds?.ToArray() ?? [],
            headers,
            hasAttachment);
    }

    private static bool HasAttachment(MessagePart? part)
    {
        if (part is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(part.Filename) || !string.IsNullOrWhiteSpace(part.Body?.AttachmentId))
        {
            return true;
        }

        return part.Parts?.Any(HasAttachment) == true;
    }

    public void Dispose()
    {
        _service?.Dispose();
        _serviceLock.Dispose();
    }
}
