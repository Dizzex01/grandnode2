using Grand.Business.Core.Interfaces.Customers;
using Grand.Data;
using Grand.Domain.Customers;
using Promotion.SpinWheel.Domain;

namespace Promotion.SpinWheel.Services;

public class SpinCouponReconciliationService(
    IRepository<CustomerSpinRecord> spinRecordRepository,
    ICustomerService customerService)
    : ISpinCouponReconciliationService
{
    public async Task EnsureApplied(string customerId)
    {
        var record = await spinRecordRepository.GetOneAsync(r => r.CustomerId == customerId);
        if (record == null) return;

        var unapplied = record.EarnedCoupons.Where(c => !c.AppliedToCart).ToList();
        if (unapplied.Count == 0) return;

        var customer = await customerService.GetCustomerById(customerId);
        if (customer == null) return;

        var changed = false;
        foreach (var earned in unapplied) {
            try {
                var applied = customer.ApplyCouponCode(SystemCustomerFieldNames.DiscountCoupons, earned.CouponCode);
                await customerService.UpdateUserField(customer, SystemCustomerFieldNames.DiscountCoupons, applied);
                earned.AppliedToCart = true;
                changed = true;
            } catch {
                // Silent — will retry on next cart load
            }
        }

        if (changed)
            await spinRecordRepository.UpdateAsync(record);
    }
}
