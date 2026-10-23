using System.Globalization;
using Application.DTOs.Licensing;
using Microsoft.Extensions.Localization;

namespace Web.Helpers;

/// <summary>Turns a <see cref="LicenseState"/> into the localized text and color shown in the banner and on the License page.</summary>
public static class LicenseMessageHelper
{
    public static string Title(LicenseState state, IStringLocalizer localizer) =>
        localizer[$"License_Status{state.Status}"].Value;

    public static string Message(LicenseState state, IStringLocalizer localizer)
    {
        string D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";

        return state.Status switch
        {
            LicenseStatus.Active => localizer[state.EnforcementDisabled ? "License_MsgDevBuild" : "License_MsgActive"].Value,
            LicenseStatus.ExpiringSoon => state.DaysLeft == 0
                ? localizer["License_MsgExpiringToday", D(state.ValidUntil)].Value
                : localizer["License_MsgExpiringSoon", state.DaysLeft ?? 0, D(state.ValidUntil)].Value,
            LicenseStatus.GracePeriod => state.DaysUntilStop == 0
                ? localizer["License_MsgGraceLastDay", D(state.ValidUntil)].Value
                : localizer["License_MsgGrace", D(state.ValidUntil), D(state.SalesStopAfter), state.DaysUntilStop ?? 0].Value,
            LicenseStatus.Expired => localizer["License_MsgExpired", D(state.ValidUntil)].Value,
            LicenseStatus.NotActivated => localizer["License_MsgNotActivated"].Value,
            LicenseStatus.Invalid => localizer["License_MsgInvalid"].Value,
            LicenseStatus.WrongMachine => localizer["License_MsgWrongMachine"].Value,
            LicenseStatus.UpdatesNotCovered => localizer["License_MsgUpdatesNotCovered", D(state.BuildDate), D(state.ValidUntil)].Value,
            _ => string.Empty
        };
    }

    /// <summary>Bootstrap color: green when fine, amber while selling still works but renewal is due, red when selling is stopped.</summary>
    public static string Severity(LicenseState state) => state.Status switch
    {
        LicenseStatus.Active => "success",
        LicenseStatus.ExpiringSoon or LicenseStatus.GracePeriod => "warning",
        _ => "danger"
    };
}
