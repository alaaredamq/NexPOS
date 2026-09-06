namespace Web;

/// <summary>
/// Marker type for the shared <c>.resx</c> string table (topbar, sidebar, cross-cutting UI
/// text), used as <c>IStringLocalizer&lt;SharedResource&gt;</c> / <c>@SharedLocalizer</c>.
///
/// Must live in the project's root namespace (<c>Web</c>), NOT under <c>Web.Resources</c>:
/// with <c>options.ResourcesPath = "Resources"</c>, the localizer factory builds the resource
/// base name as <c>&lt;rootNamespace&gt;.Resources.&lt;typeNameWithoutRootNamespace&gt;</c>.
/// From <c>Web.SharedResource</c> that is <c>Web.Resources.SharedResource</c> — matching
/// <c>Resources/SharedResource.{culture}.resx</c>. From <c>Web.Resources.SharedResource</c> it
/// would be <c>Web.Resources.Resources.SharedResource</c>, which doesn't exist, and every
/// lookup would silently return the key instead of the translation.
/// </summary>
public class SharedResource
{
}
