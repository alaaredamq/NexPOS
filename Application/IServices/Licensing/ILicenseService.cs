using Application.DTOs.Licensing;

namespace Application.IServices.Licensing
{
    /// <summary>
    /// Offline license check: a signed key bound to this PC. See WORKLOG "L1" for the design.
    /// </summary>
    public interface ILicenseService
    {
        /// <summary>The current license state. Cheap: safe to call on every request.</summary>
        LicenseState GetState();

        /// <summary>Validates a pasted license key and, if it is valid for this PC and not older than the current one, saves it.</summary>
        LicenseActivationResult Activate(string licenseKeyText);

        /// <summary>Throws <see cref="Application.Exceptions.LicenseRequiredException"/> unless new sales are allowed.</summary>
        void EnsureSalesAllowed();
    }
}
