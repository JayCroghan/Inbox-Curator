using System.Net;
using Google;
using Google.Apis.Gmail.v1;
using InboxCurator.Gmail;

namespace InboxCurator.Tests;

public sealed class GmailSafetyTests
{
    [Fact]
    public void OAuthConfiguration_RequestsExactlyGmailReadonly()
    {
        var scope = Assert.Single(InstalledAppCredentialProvider.AuthorizedScopes);
        Assert.Equal(GmailService.Scope.GmailReadonly, scope);
        Assert.DoesNotContain("modify", scope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attachmentId", GmailMailboxClient.AttachmentStructureFields, StringComparison.Ordinal);
        Assert.DoesNotContain("data", GmailMailboxClient.AttachmentStructureFields, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            GmailMailboxClient.AttachmentStructureFields.Count(character => character == '('),
            GmailMailboxClient.AttachmentStructureFields.Count(character => character == ')'));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void RetryClassification_IsLimitedToThrottleAndTransientFailures(HttpStatusCode status, bool expected)
    {
        var exception = new GoogleApiException("Gmail", "synthetic") { HttpStatusCode = status };
        Assert.Equal(expected, GmailMailboxClient.IsTransient(exception));
    }

    [Fact]
    public void RetryDelay_IsExponentialAndCapped()
    {
        var delay = new SystemRetryDelay();

        Assert.InRange(delay.GetDelay(0), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.3));
        Assert.InRange(delay.GetDelay(3), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8.3));
        Assert.InRange(delay.GetDelay(20), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60.3));
    }
}
