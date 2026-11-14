using System.Text.RegularExpressions;

namespace Sync;

/// <summary>
/// Reads the connection link the owner pastes. Forgiving on purpose: people paste whatever they copied, which from
/// WhatsApp is the whole message (instructions, Arabic text and the link), often with a trailing full stop or spaces.
/// </summary>
public static class EnrollmentLink
{
    public sealed record Parsed(Uri BaseUri, string Code);

    public enum Problem { None, NotALink, Insecure }

    private static readonly Regex UrlInText = new(@"https?://[^\s<>""'«»]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Problem TryParse(string? input, out Parsed? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(input)) return Problem.NotALink;

        var text = input.Trim();
        var match = UrlInText.Match(text);
        var candidate = match.Success ? match.Value : text;
        candidate = candidate.TrimEnd('.', ',', ';', ')', ']', '!', '?', '»');

        // "cloud.example.com/enroll/CODE" without a scheme.
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return Problem.NotALink;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return Problem.NotALink;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.FindIndex(segments, s => s.Equals("enroll", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= segments.Length) return Problem.NotALink;

        var code = new string(Uri.UnescapeDataString(segments[at + 1]).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (code.Length != 12) return Problem.NotALink;

        // The code buys a credential, so never send it over plain http to anything but this computer.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback) return Problem.Insecure;

        var prefix = string.Join('/', segments.Take(at));
        var baseUri = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port, prefix.Length == 0 ? "/" : "/" + prefix + "/").Uri;
        parsed = new Parsed(baseUri, code);
        return Problem.None;
    }
}
