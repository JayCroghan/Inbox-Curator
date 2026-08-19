using InboxCurator.Services;

namespace InboxCurator.Tests;

public sealed class AddressNormalizerTests
{
    [Theory]
    [InlineData("Signal Dispatch <News.Example.COM>", "news.example.com")]
    [InlineData("<weekly.example>", "weekly.example")]
    [InlineData(" alerts.example ", "alerts.example")]
    public void NormalizeListId_ExtractsCanonicalIdentifier(string input, string expected)
    {
        Assert.Equal(expected, AddressNormalizer.NormalizeListId(input));
    }

    [Fact]
    public void ParseSingle_NormalizesCaseAndPreservesDisplayName()
    {
        var parsed = AddressNormalizer.ParseSingle("Mina Chen <MINA.CHEN@Example.Test>");

        Assert.Equal("Mina Chen", parsed.DisplayName);
        Assert.Equal("mina.chen@example.test", parsed.Normalized);
    }

    [Fact]
    public void ParseMany_DeduplicatesRecipients()
    {
        var parsed = AddressNormalizer.ParseMany(
            "Mina <mina@example.test>, Jo <jo@example.test>",
            "MINA@example.test");

        Assert.Equal(2, parsed.Count);
        Assert.Contains("mina@example.test", parsed);
        Assert.Contains("jo@example.test", parsed);
    }
}
