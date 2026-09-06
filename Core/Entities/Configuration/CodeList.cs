using Core.Entities.Base;

namespace Core.Entities.Configuration;

public class CodeList : IEntityBase<int>
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowMultipleSelection { get; set; } = false;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<CodeListValue> Values { get; set; } = new List<CodeListValue>();
}
