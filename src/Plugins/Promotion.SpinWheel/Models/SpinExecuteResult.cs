namespace Promotion.SpinWheel.Models;

public class SpinExecuteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int WinningSegmentIndex { get; set; }
    public string CouponCode { get; set; } = string.Empty;
    public string DiscountLabel { get; set; } = string.Empty;
    public DateTime NextSpinAt { get; set; }
}
