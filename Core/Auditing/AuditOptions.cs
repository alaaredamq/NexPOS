namespace Core.Auditing;

/// <summary>Bound from the "Audit" section of appsettings.json.</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// Request paths (prefix match) that are not logged: static assets, the high-frequency polling /
    /// search-as-you-type endpoints that would bury the real events, and the audit viewer's own AJAX
    /// calls (otherwise reading the log would write a row per page-turn). Data changes and errors raised
    /// while serving them are still recorded. Anything listed in appsettings.json is *added* to these.
    /// </summary>
    public List<string> ExcludedPaths { get; set; } =
    [
        "/branding",
        "/health",
        "/favicon",
        "/lib/",
        "/js/",
        "/css/",
        "/images/",
        "/fonts/",
        "/Metronics/",
        "/Notifications/GetUnreadCount",
        "/POS/SearchProduct",
        "/POS/AutocompleteProducts",
        "/POS/SearchProductsForSelect2",
        "/AuditLogs/GetData",
        "/AuditLogs/Details"
    ];

    /// <summary>
    /// Also record requests that only <i>read</i> something (opening a page, loading a list) and succeeded. Off by default:
    /// they say little and are by far the most numerous rows, so they only make the audit database grow. Reads that
    /// failed or were refused are always recorded, and so is everything that changes data.
    /// </summary>
    public bool LogReadRequests { get; set; }

    /// <summary>Record the (redacted) JSON body of API/AJAX posts, so "what exactly was submitted" is answerable.</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Largest request body (bytes) that is stored; longer bodies are cut with a marker.</summary>
    public int MaxBodyBytes { get; set; } = 8192;

    /// <summary>
    /// Entries older than this many days are deleted (and the purge is itself logged).
    /// 0 = keep everything forever (default): nothing is ever removed unless somebody opts in.
    /// </summary>
    public int RetentionDays { get; set; }
}
