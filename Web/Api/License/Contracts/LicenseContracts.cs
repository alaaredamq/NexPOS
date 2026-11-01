using System.ComponentModel.DataAnnotations;

namespace Web.Api.License.Contracts;

/// <summary>
/// The licence screen: what is installed, what it allows, and this PC's Machine ID (which the customer sends to the issuer).
/// The same fields the Razor License page showed; the shell's banner reads the shorter <c>license</c> block of <c>/session</c>.
/// </summary>
public sealed record LicenseDto
{
    /// <summary><c>Active</c>, <c>ExpiringSoon</c>, <c>GracePeriod</c>, <c>Expired</c>, <c>NotActivated</c>, <c>Invalid</c>, <c>WrongMachine</c> or <c>UpdatesNotCovered</c>.</summary>
    public required string Status { get; init; }

    /// <summary><c>success</c>, <c>warning</c> or <c>danger</c>.</summary>
    public required string Severity { get; init; }

    /// <summary>Localized headline of the status.</summary>
    public required string Title { get; init; }

    /// <summary>Localized sentence that says what the status means for this shop (dates included).</summary>
    public required string Message { get; init; }

    public required bool AllowsSales { get; init; }

    public required bool ShowsWarning { get; init; }

    /// <summary>A Debug build: nothing is enforced.</summary>
    public required bool EnforcementDisabled { get; init; }

    /// <summary>This computer's id, <c>XXXXX-XXXXX-XXXXX-XXXXX</c>.</summary>
    public required string MachineId { get; init; }

    /// <summary><c>yyyy-MM-dd</c>: when this build was released (a perpetual licence only covers builds up to its updates date).</summary>
    public required string BuildDate { get; init; }

    /// <summary>Present once a licence is installed.</summary>
    public string? Customer { get; init; }

    /// <summary><c>Full</c>, <c>Provisional</c> or <c>Trial</c>; null while nothing is installed.</summary>
    public string? Kind { get; init; }

    public string? Serial { get; init; }

    /// <summary><c>yyyy-MM-dd</c>.</summary>
    public string? IssuedOn { get; init; }

    /// <summary><c>yyyy-MM-dd</c>. A time-limited licence: the last valid day. A full one: the last day of its updates plan.</summary>
    public string? ValidUntil { get; init; }

    /// <summary>True for <c>Provisional</c> and <c>Trial</c>: <see cref="ValidUntil"/> is then an expiry, not an updates date.</summary>
    public required bool IsTimeLimited { get; init; }

    public int? DaysLeft { get; init; }

    public int? DaysUntilStop { get; init; }

    /// <summary><c>yyyy-MM-dd</c>; time-limited licences: the last day selling still works.</summary>
    public string? SalesStopAfter { get; init; }
}

/// <summary>Body of "activate a licence": the key as it was sent to the customer, pasted whole.</summary>
public sealed record LicenseActivateRequest
{
    [Required]
    [StringLength(4000, MinimumLength = 1)]
    public required string LicenseKey { get; init; }
}
