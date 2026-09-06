using Core.Entities.Base;

namespace Core.Entities.Configuration;

public class CodeListValue : IEntityBase<int>
{
    public int Id { get; set; }
    public int CodeListId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? DisplayText { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string? Metadata { get; set; } // JSON for additional data
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual CodeList CodeList { get; set; } = null!;
}
