using Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace Core.Entities.Configuration
{
    public class SystemSettings : BaseEntity, IEntityBase<int>
    {
        [Required]
        [MaxLength(100)]
        public string SettingKey { get; set; } = string.Empty;

        [Required]
        public string SettingValue { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string SettingType { get; set; } = string.Empty; // string, int, bool, decimal, json

        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public bool IsEditable { get; set; } = true;

        public bool IsEncrypted { get; set; } = false;
    }
}
