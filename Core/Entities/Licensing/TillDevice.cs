using Core.Entities.Base;
using System.ComponentModel.DataAnnotations;

namespace Core.Entities.Licensing
{
    /// <summary>
    /// A till PC (a browser on another computer) that is allowed to sell through this store server. The server's own PC is not
    /// listed: it always counts as the first seat of the licence. A row is created when an administrator asks to add a till
    /// (it then holds a one-time pairing code) and becomes active when the till redeems the code and receives its device token.
    /// Only hashes are stored, so a copy of the database cannot be used to impersonate a till. Never synced to the cloud.
    /// </summary>
    public class TillDevice : BaseEntity, IEntityBase<int>
    {
        /// <summary>What the administrator calls it ("Till 2", "Back counter"); shown on the License page.</summary>
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>SHA-256 (hex) of the one-time pairing code; null once the code has been used.</summary>
        [MaxLength(64)]
        public string? PairingCodeHash { get; set; }

        public DateTime? PairingExpiresAt { get; set; }

        /// <summary>SHA-256 (hex) of the device token; null while the till has not paired yet.</summary>
        [MaxLength(64)]
        public string? TokenHash { get; set; }

        public DateTime? PairedAt { get; set; }

        public DateTime? LastSeenAt { get; set; }

        /// <summary>Set when an administrator removes the till; frees its seat.</summary>
        public DateTime? RevokedAt { get; set; }

        public bool IsActive => TokenHash != null && RevokedAt == null;
    }
}
