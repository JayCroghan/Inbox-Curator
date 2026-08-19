using System.Net.Mail;
using System.Text.RegularExpressions;

namespace InboxCurator.Services;

public static partial class AddressNormalizer
{
    public static ParsedAddress ParseSingle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ParsedAddress(null, "unknown@invalid", "unknown@invalid");
        }

        try
        {
            var address = new MailAddress(value.Trim());
            return new ParsedAddress(
                NullIfEmpty(address.DisplayName),
                address.Address,
                NormalizeAddress(address.Address));
        }
        catch (FormatException)
        {
            var match = EmailAddressRegex().Match(value);
            var fallback = match.Success ? match.Value : value.Trim();
            return new ParsedAddress(null, fallback, NormalizeAddress(fallback));
        }
    }

    public static IReadOnlyCollection<string> ParseMany(params string?[] values)
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            foreach (Match match in EmailAddressRegex().Matches(value!))
            {
                addresses.Add(NormalizeAddress(match.Value));
            }
        }

        return addresses;
    }

    public static string NormalizeAddress(string value)
    {
        var normalized = value.Trim().Trim('<', '>').ToLowerInvariant();
        var at = normalized.LastIndexOf('@');
        if (at <= 0 || at == normalized.Length - 1)
        {
            return normalized;
        }

        return $"{normalized[..at]}@{normalized[(at + 1)..].TrimEnd('.')}";
    }

    public static string? NormalizeListId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var start = trimmed.LastIndexOf('<');
        var end = trimmed.IndexOf('>', Math.Max(0, start));
        var identifier = start >= 0 && end > start
            ? trimmed[(start + 1)..end]
            : trimmed;

        identifier = identifier.Trim().Trim('<', '>').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddressRegex();
}

public sealed record ParsedAddress(string? DisplayName, string Original, string Normalized);
