using Application.DTOs.Licensing;

namespace Application.Exceptions;

/// <summary>
/// Thrown when a new sale is attempted while the license does not allow selling
/// (expired past the grace week, not activated, wrong computer, ...).
/// </summary>
public class LicenseRequiredException : InvalidOperationException
{
    public LicenseStatus Status { get; }

    public LicenseRequiredException(LicenseStatus status)
        : base($"New sales are stopped because the license is not active ({status}). Open the License page to renew or activate.")
    {
        Status = status;
    }
}
