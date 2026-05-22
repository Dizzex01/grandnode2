using Grand.Domain;

namespace Promotion.SpinWheel.Domain;

public class CustomerSpinRecord : BaseEntity
{
    public string CustomerId { get; set; } = string.Empty;
    public DateTime LastSpinUtc { get; set; } = DateTime.MinValue;
    public int TotalSpins { get; set; }
}
