using System.ComponentModel.DataAnnotations;
using Core.Entities.Base;

namespace Core.Entities.Products
{
    public class ProductDisplayOrder : IEntityBase<int>
    {
        public int Id { get; set; }
        
        [Required]
        public int ProductId { get; set; }
        
        public Product Product { get; set; }
        
        [Required]
        public int DisplayOrder { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
