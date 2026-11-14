namespace Web.Api.Live;

/// <summary>
/// A counter that moves every time a request changes data. Several PCs share one store database, so a till that is open on the
/// POS screen has no way to know another till just sold; it asks this counter every few seconds and, when it moved, reloads what it shows.
/// <para>
/// The <see cref="Version"/> text is <c>epoch.count</c>. The epoch is new on every start of the server, so a restart (the count falls back
/// to 0) is still seen as a change instead of looking like "nothing happened".
/// </para>
/// </summary>
public sealed class DataVersion
{
    private readonly string _epoch = Guid.NewGuid().ToString("N")[..8];
    private long _count;

    public string Version => $"{_epoch}.{Interlocked.Read(ref _count)}";

    public void Bump() => Interlocked.Increment(ref _count);
}
