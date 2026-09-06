using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Hosting;

namespace Web.Services;

/// <summary>
/// Loads the JS translation tables (wwwroot/js/locales/{lang}.json) once at startup and hands
/// them out as pre-built <see cref="HtmlString"/> — avoids a synchronous file read on every
/// layout render.
/// </summary>
public interface ILocaleCatalog
{
    /// <summary>Raw JSON for the language, already safe to embed in a &lt;script&gt; block.</summary>
    IHtmlContent Json(string language);
}

public sealed class LocaleCatalog : ILocaleCatalog
{
    private const string DefaultLanguage = "en";
    private readonly IReadOnlyDictionary<string, HtmlString> _byLanguage;

    public LocaleCatalog(IHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "wwwroot", "js", "locales");
        var map = new Dictionary<string, HtmlString>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var lang = Path.GetFileNameWithoutExtension(file);
                try
                {
                    map[lang] = new HtmlString(File.ReadAllText(file));
                }
                catch
                {
                    // Skip an unreadable/locked locale file rather than failing startup.
                }
            }
        }

        if (!map.ContainsKey(DefaultLanguage))
        {
            map[DefaultLanguage] = new HtmlString("{}");
        }

        _byLanguage = map;
    }

    public IHtmlContent Json(string language)
    {
        if (!string.IsNullOrEmpty(language) && _byLanguage.TryGetValue(language, out var json))
        {
            return json;
        }

        return _byLanguage[DefaultLanguage];
    }
}
