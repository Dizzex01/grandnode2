using Grand.Domain;

namespace Promotion.SpinWheel.Domain;

public class CustomerSpinRecord : BaseEntity
{
    public string CustomerId { get; set; } = string.Empty;
    public DateTime LastSpinUtc { get; set; } = DateTime.MinValue;
    public int TotalSpins { get; set; }
    public List<SpinEarnedCoupon> EarnedCoupons { get; set; } = new();
}

public class SpinEarnedCoupon
{
    public string CouponCode { get; set; } = string.Empty;
    public string DiscountLabel { get; set; } = string.Empty;
    public DateTime EarnedAtUtc { get; set; }
    public bool AppliedToCart { get; set; }
}
