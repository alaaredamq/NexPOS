using System.Globalization;
using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Common.Licensing;
using Microsoft.Extensions.Localization;
using Web.Api.License.Contracts;
using Web.Helpers;

namespace Web.Api.License;

public interface ILicenseApiService
{
    LicenseDto Get();

    /// <summary>Installs the key. Throws <see cref="LicenseActivationException"/> (with a localized reason) when it is refused; the installed licence is then unchanged.</summary>
    LicenseDto Activate(string licenseKey);
}

/// <summary>The key was refused; <see cref="Message"/> says why in the user's language.</summary>
public sealed class LicenseActivationException : Exception
{
    public LicenseActivationException(string message) : base(message)
    {
    }
}

/// <summary>Maps <see cref="ILicenseService"/> to the licence screen's DTO, with the same localized texts as the shell's banner (<see cref="LicenseMessageHelper"/>).</summary>
public sealed class LicenseApiService : ILicenseApiService
{
    private readonly ILicenseService _license;
    private readonly IStringLocalizer<SharedResource> _text;

    public LicenseApiService(ILicenseService license, IStringLocalizer<SharedResource> text)
    {
        _license = license;
        _text = text;
    }

    public LicenseDto Get() => ToDto(_license.GetState());

    public LicenseDto Activate(string licenseKey)
    {
        var result = _license.Activate(licenseKey);
        if (!result.Success)
        {
            throw new LicenseActivationException(_text[$"License_Error_{result.Error}"].Value);
        }

        return ToDto(result.State);
    }

    private LicenseDto ToDto(LicenseState state)
    {
        static string? Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new LicenseDto
        {
            Status = state.Status.ToString(),
            Severity = LicenseMessageHelper.Severity(state),
            Title = LicenseMessageHelper.Title(state, _text),
            Message = LicenseMessageHelper.Message(state, _text),
            AllowsSales = state.AllowsSales,
            ShowsWarning = state.ShowsWarning,
            EnforcementDisabled = state.EnforcementDisabled,
            MachineId = state.MachineId,
            BuildDate = Day(state.BuildDate)!,
            Customer = state.Customer,
            Kind = state.Kind?.ToString(),
            Serial = state.Serial,
            IssuedOn = Day(state.IssuedOn),
            ValidUntil = Day(state.ValidUntil),
            IsTimeLimited = state.Kind is LicenseKind.Provisional or LicenseKind.Trial,
            DaysLeft = state.DaysLeft,
            DaysUntilStop = state.DaysUntilStop,
            SalesStopAfter = Day(state.SalesStopAfter),
        };
    }
}
