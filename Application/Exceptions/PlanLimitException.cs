using Common.Licensing;

namespace Application.Exceptions;

/// <summary>
/// Thrown when an action needs more than the installed plan includes, such as a fourth active user on Basic.
/// Nothing has been changed when it is thrown.
/// </summary>
public class PlanLimitException : InvalidOperationException
{
    public ProFeature Feature { get; }

    /// <summary>The limit that was hit (for <see cref="ProFeature.MoreUsers"/>: the most active users), for the message.</summary>
    public int Limit { get; }

    public PlanLimitException(ProFeature feature, int limit)
        : base($"The Basic plan allows at most {limit} for {feature}. Upgrade to Pro for more.")
    {
        Feature = feature;
        Limit = limit;
    }
}
