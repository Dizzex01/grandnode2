namespace Promotion.SpinWheel.Services;

public interface ISpinCouponReconciliationService
{
    Task EnsureApplied(string customerId);
}
