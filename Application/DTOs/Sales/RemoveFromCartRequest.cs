namespace Application.DTOs.Sales
{
    public class RemoveFromCartRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public int ProductId { get; set; }
    }
}
