namespace Sync;

/// <summary>
/// The Coduo POS Cloud this installation connects to. Live installs use <see cref="Live"/>; a test install sets
/// <c>Online:Server</c> (appsettings.json, or the <c>Online__Server</c> environment variable) to the test site, e.g.
/// https://nexpos.tryasp.net. The pasted connection link only carries the code: a link for any other server is refused, so a
/// live shop can never be connected to the test site by mistake (or the other way round).
/// </summary>
public sealed record OnlineServer(Uri BaseUri)
{
    public const string Live = "https://coduos.com";
    public const string ConfigKey = OnlineSettings.SectionName + ":Server";

    public string Host => BaseUri.Host;

    public static OnlineServer From(string? configured)
    {
        var text = string.IsNullOrWhiteSpace(configured) ? Live : configured.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            throw new InvalidOperationException($"{ConfigKey} must be an https:// address, not '{configured}'.");
        return new OnlineServer(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"));
    }

    /// <summary>
    /// True when a link points at this server (www. or not). A link to this computer is always allowed, so a developer can
    /// connect to a Cloud running locally without changing the setting.
    /// </summary>
    public bool Accepts(Uri link) =>
        link.IsLoopback || string.Equals(Bare(link.Host), Bare(BaseUri.Host), StringComparison.OrdinalIgnoreCase);

    private static string Bare(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
