using System.Collections.Concurrent;
using System.Globalization;
using Application.IServices.Sales;
using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Printing;
using Microsoft.Extensions.Localization;
using Sync;
using Web.Api.Online.Contracts;

namespace Web.Api.Online;

public interface IOnlineSealingApiService
{
    /// <summary>The recovery slip. Makes the key on first use. Needs the signed-in user's PIN.</summary>
    /// <exception cref="OnlineRuleException">Wrong PIN, or too many wrong PINs.</exception>
    Task<RecoverySlipDto> GetSlipAsync(int userId, string pin);

    /// <summary>Prints the slip on the receipt printer. Never fails because of the printer: <c>Printed</c> says what happened.</summary>
    Task<SlipPrintResultDto> PrintSlipAsync(int userId, string pin);

    Task<(byte[] Pdf, string FileName)> GetSlipPdfAsync(int userId, string pin);

    /// <summary>The owner kept the slip: sealing starts and the next upload replaces the cloud copy with a sealed one.</summary>
    /// <exception cref="OnlineRuleException">409 <c>not_connected</c> or <c>no_key</c>.</exception>
    OnlineStatusDto Confirm(int userId);

    /// <summary>Puts the key from a slip on this PC. 400 <c>recovery_code_invalid</c>, 409 <c>key_exists</c>.</summary>
    Task<OnlineStatusDto> RestoreAsync(int userId, string pin, string code);
}

/// <summary>A rule of private Online refused the request. <see cref="Field"/> is set when it is a field of the request (the PIN).</summary>
public sealed class OnlineRuleException(int status, string code, string message, string? field = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
}

/// <summary>
/// The recovery slip and the key behind it. Everything that reveals the key re-asks the signed-in user's PIN, throttled: a PIN has
/// only 10,000 values, so five wrong ones lock the check for five minutes. The code is never written to the audit trail.
/// </summary>
public sealed class OnlineSealingApiService : IOnlineSealingApiService
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<int, (int Failures, DateTime LockedUntil)> Attempts = new();

    private readonly IOnlineSealing _sealing;
    private readonly IOnlineApiService _online;
    private readonly IOnlineConnection _connection;
    private readonly IUserService _users;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;

    public OnlineSealingApiService(IOnlineSealing sealing, IOnlineApiService online, IOnlineConnection connection, IUserService users,
        IThermalPrinterService printer, IAuditTrail audit, IStringLocalizer<SharedResource> text)
    {
        _sealing = sealing;
        _online = online;
        _connection = connection;
        _users = users;
        _printer = printer;
        _audit = audit;
        _text = text;
    }

    /// <summary>Forgets the failure counts (tests, and nothing else).</summary>
    internal static void ResetAttempts() => Attempts.Clear();

    public async Task<RecoverySlipDto> GetSlipAsync(int userId, string pin)
    {
        await RequirePinAsync(userId, pin);
        var slip = _sealing.GetSlip();
        Record(AuditActions.RecoverySlipShown, "The recovery code was shown", userId, slip.KeyId);
        return new RecoverySlipDto { Code = slip.Code, Groups = slip.Groups, KeyId = slip.KeyId };
    }

    public async Task<SlipPrintResultDto> PrintSlipAsync(int userId, string pin)
    {
        await RequirePinAsync(userId, pin);
        var slip = _sealing.GetSlip();
        var document = BuildDocument(slip);

        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);
        _audit.LogBusiness(printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob", printed ? AuditOutcome.Success : AuditOutcome.Failure, printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title, details: new { document.Title, slip.KeyId });

        return new SlipPrintResultDto { Printed = printed, Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value };
    }

    public async Task<(byte[] Pdf, string FileName)> GetSlipPdfAsync(int userId, string pin)
    {
        await RequirePinAsync(userId, pin);
        var slip = _sealing.GetSlip();
        var document = BuildDocument(slip);

        var pdf = _printer.GeneratePdf(document, PdfPaper.A4);
        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title, details: new { document.Title, slip.KeyId, bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    public OnlineStatusDto Confirm(int userId)
    {
        try
        {
            _sealing.Confirm();
        }
        catch (OnlineSealingException ex)
        {
            throw new OnlineRuleException(StatusCodes.Status409Conflict, ex.Code, _text["Online_Seal_" + ex.Code].Value);
        }

        var keyId = _online.Get().SealKeyId;
        Record(AuditActions.SealingConfirmed, "Private Online switched on: the owner kept the recovery slip", userId, keyId);
        return _online.Get();
    }

    public async Task<OnlineStatusDto> RestoreAsync(int userId, string pin, string code)
    {
        await RequirePinAsync(userId, pin);
        switch (_sealing.RestoreKey(code))
        {
            case RestoreOutcome.InvalidCode:
                throw new OnlineRuleException(StatusCodes.Status400BadRequest, "recovery_code_invalid", _text["Online_Seal_recovery_code_invalid"].Value, "code");
            case RestoreOutcome.DifferentKeyHere:
                throw new OnlineRuleException(StatusCodes.Status409Conflict, "key_exists", _text["Online_Seal_key_exists"].Value);
        }

        Record(AuditActions.SealKeyRestored, "A key from a recovery slip was put on this PC", userId, _online.Get().SealKeyId);
        return _online.Get();
    }

    // ---- PIN ---------------------------------------------------------------------------------------------------

    private async Task RequirePinAsync(int userId, string pin)
    {
        var now = DateTime.UtcNow;
        if (Attempts.TryGetValue(userId, out var state) && state.LockedUntil > now)
            throw new OnlineRuleException(StatusCodes.Status429TooManyRequests, "pin_locked", _text["Online_Seal_pin_locked"].Value);

        var user = await FindAsync(userId);
        if (user is not null && await _users.ValidatePinAsync(user, pin))
        {
            Attempts.TryRemove(userId, out _);
            return;
        }

        // A lock that has run out starts the count again; a count that never locked keeps growing.
        var prior = Attempts.TryGetValue(userId, out var seen) ? seen : (Failures: 0, LockedUntil: default(DateTime));
        var failures = (prior.LockedUntil != default && prior.LockedUntil <= now ? 0 : prior.Failures) + 1;
        Attempts[userId] = failures >= MaxFailures ? (0, now + Lockout) : (failures, default);

        _audit.LogSecurity(AuditActions.RecoverySlipFailed, "The recovery code was refused: the PIN was incorrect",
            AuditOutcome.Failure, AuditLogSeverity.Warning, entityId: userId, entityLabel: user?.Username);
        throw new OnlineRuleException(StatusCodes.Status400BadRequest, "invalid_pin", _text["Online_Seal_wrong_pin"].Value, "pin");
    }

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<Core.Entities.Users.User?> FindAsync(int id)
    {
        try { return await _users.GetByIdAsync(id); }
        catch (ArgumentNullException) { return null; }
    }

    private void Record(string action, string description, int userId, string? keyId)
        => _audit.LogEvent(AuditCategory.Security, action, description, "OnlineSealing", userId: userId,
            entityLabel: keyId, details: new { keyId });

    // ---- the slip ----------------------------------------------------------------------------------------------

    /// <summary>
    /// What is printed or saved. Three groups a line so it fits 80 mm paper; short lines, since receipt paper does not wrap
    /// for you. The words come from the language the screen is in. The code is on the paper only: no log has it.
    /// </summary>
    private PrintDocument BuildDocument(RecoverySlipInfo slip)
    {
        var info = _connection.GetStatus();
        var lines = new List<PrintLine>
        {
            PrintLine.Header(_text["Online_Slip_Title"].Value),
            PrintLine.Centered("NexPOS Online"),
        };
        if (!string.IsNullOrWhiteSpace(info.ClientName)) lines.Add(PrintLine.Centered(info.ClientName));
        lines.Add(PrintLine.Separator());
        lines.Add(PrintLine.Bold(_text["Online_Slip_Code"].Value));
        lines.Add(PrintLine.Spacer());
        for (var i = 0; i < slip.Groups.Count; i += 3)
            lines.Add(PrintLine.Centered(string.Join("  ", slip.Groups.Skip(i).Take(3))));
        lines.Add(PrintLine.Spacer());
        lines.Add(PrintLine.Separator());
        foreach (var key in new[] { "Online_Slip_Keep1", "Online_Slip_Keep2", "Online_Slip_Keep3", "Online_Slip_Use1", "Online_Slip_Use2" })
            lines.Add(PrintLine.Plain(_text[key].Value));
        lines.Add(PrintLine.Separator());
        lines.Add(PrintLine.Row(_text["Online_Slip_KeyId"].Value, slip.KeyId));
        lines.Add(PrintLine.Row(_text["Online_Slip_Printed"].Value, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        return new PrintDocument(_text["Online_Slip_Title"].Value, lines);
    }
}
