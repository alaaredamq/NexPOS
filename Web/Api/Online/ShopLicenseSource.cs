using Application.Services.Licensing;
using Common.Licensing;
using Sync;

namespace Web.Api.Online;

/// <summary>
/// The licence key the sync service hands the cloud on its regular check-in. The cloud verifies the issuer's signature itself,
/// so it learns the plan from the key, not from anything this PC could simply claim.
/// </summary>
public sealed class ShopLicenseSource(ILicenseStorage storage) : IShopLicenseSource
{
    public string? LicenseKey
    {
        get
        {
            try
            {
                var text = storage.Read();
                return string.IsNullOrWhiteSpace(text) ? null : LicenseCodec.Canonicalize(text);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
