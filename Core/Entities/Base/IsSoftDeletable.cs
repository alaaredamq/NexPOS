namespace Core.Entities.Base;

public abstract class ISSoftDeletable : BaseEntity  {
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}