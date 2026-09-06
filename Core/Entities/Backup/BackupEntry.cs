using Core.Entities.Base;

namespace Core.Entities.Backup;

public class BackupEntry : ISSoftDeletable, IEntityBase<int>
{
    public string FilePath { get; set; } = null!;
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public BackupEntry(string filePath, int? createdByUserId)
    {
        FilePath = filePath;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    protected BackupEntry() { }
}


