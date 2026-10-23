using Core.Entities.Logging;

namespace Application.DTOs.Logging;

/// <summary>One field of a record that changed, shown as old -> new in the viewer.</summary>
public sealed record AuditFieldChange(string Field, string? Old, string? New);

/// <summary>A single audit entry plus everything the detail view needs to explain it.</summary>
public sealed class AuditLogDetails
{
    public required AuditLog Entry { get; init; }

    /// <summary>Field-by-field changes (updates), the saved values (creates) or the removed values (deletes).</summary>
    public List<AuditFieldChange> Changes { get; init; } = [];

    /// <summary>How many rows in total belong to the same HTTP request (including this one).</summary>
    public int RowsInSameRequest { get; init; }
}
