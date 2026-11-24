namespace Cloud.Data;

/// <summary>
/// A Coduo POS licence key the provider issued (the offline key that lets a shop PC sell), as the cloud knows it. It comes from the
/// portal's own issuer, from the desktop keygen reporting what it signed, from a pasted key, from an imported issued.csv, or from a
/// shop PC that showed its key at check-in. Linked to the client (and the branch with the same Machine ID) so renewals and
/// payments sit with the rest of the client. Keys are offline: deleting a record here does not stop a key working.
/// </summary>
public class License
{
    public int Id { get; set; }
    /// <summary>Null until it is matched to a client (an imported key with an unknown customer name).</summary>
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>The branch whose shop PC has this Machine ID, when known.</summary>
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
    /// <summary>XXXXX-XXXXX-XXXXX-XXXXX, the PC the key is bound to.</summary>
    public string MachineId { get; set; } = "";
    /// <summary>Full, Provisional or Trial (Common.Licensing.LicenseKind).</summary>
    public string Kind { get; set; } = "";
    /// <summary>Basic or Pro.</summary>
    public string Plan { get; set; } = "";
    public int Seats { get; set; } = 1;
    public DateOnly IssuedOn { get; set; }
    /// <summary>Full: last day of the updates plan. Provisional/Trial: last valid day.</summary>
    public DateOnly ValidUntil { get; set; }
    /// <summary>The key's serial (8 hex digits), as issued.csv and the issuer print it.</summary>
    public string Serial { get; set; } = "";
    /// <summary>The customer name written in the key.</summary>
    public string Customer { get; set; } = "";
    /// <summary>The key itself (NXL1-...), so it can be sent again. Null when only its details are known (imported, or seen at a shop PC).</summary>
    public string? KeyText { get; set; }
    /// <summary>Cloud, Keygen, Pasted, Imported or Till: where the cloud learnt of it.</summary>
    public string Source { get; set; } = "";
    /// <summary>The payment recorded for it, when there was one.</summary>
    public int? PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public static class LicenseSources
{
    public const string Cloud = "Cloud";
    public const string Keygen = "Keygen";
    public const string Pasted = "Pasted";
    public const string Imported = "Imported";
    public const string Till = "Till";
}
