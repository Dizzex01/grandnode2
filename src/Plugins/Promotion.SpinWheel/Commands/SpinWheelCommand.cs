using Grand.Business.Core.Interfaces.Catalog.Discounts;
using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Business.Core.Interfaces.Customers;
using Grand.Data;
using Grand.Domain.Customers;
using Grand.Domain.Discounts;
using MediatR;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Models;

namespace Promotion.SpinWheel.Commands;

public class SpinWheelCommand : IRequest<SpinExecuteResult>
{
    public string CustomerId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
}

public class SpinWheelCommandHandler(
    IRepository<CustomerSpinRecord> spinRecordRepository,
    ISettingService settingService,
    IDiscountService discountService,
    ICustomerService customerService)
    : IRequestHandler<SpinWheelCommand, SpinExecuteResult>
{
    public async Task<SpinExecuteResult> Handle(SpinWheelCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingService.LoadSetting<SpinWheelSettings>(request.StoreId);

        if (!settings.Enabled || settings.Segments.Count == 0)
            return Fail("Spin wheel is not available.");

        var record = await spinRecordRepository.GetOneAsync(r => r.CustomerId == request.CustomerId);
        var now = DateTime.UtcNow;

        if (record != null) {
            var nextAllowed = record.LastSpinUtc.AddHours(settings.CooldownHours);
            if (now < nextAllowed)
                return Fail($"Cooldown active. Next spin at {nextAllowed:u}.");
        }

        var segment = PickWeightedSegment(settings.Segments);
        var segmentIndex = settings.Segments.IndexOf(segment);
        var couponCode = $"{segment.CouponPrefix}-{GenerateAlphanumeric(6)}";

        var discount = new Discount {
            Name = $"SpinWheel - {segment.Label}",
            DiscountTypeId = DiscountType.AssignedToOrderTotal,
            UsePercentage = true,
            DiscountPercentage = (double)segment.DiscountPercent,
            RequiresCouponCode = true,
            IsEnabled = true,
            Reused = false,
            IsCumulative = false,
            StartDateUtc = now
        };
        await discountService.InsertDiscount(discount);

        await discountService.InsertDiscountCoupon(new DiscountCoupon {
            DiscountId = discount.Id,
            CouponCode = couponCode,
            Used = false
        });

        var earnedCoupon = new SpinEarnedCoupon {
            CouponCode = couponCode,
            DiscountLabel = segment.Label,
            EarnedAtUtc = now,
            AppliedToCart = false
        };

        if (record == null) {
            record = new CustomerSpinRecord {
                CustomerId = request.CustomerId,
                LastSpinUtc = now,
                TotalSpins = 1,
                EarnedCoupons = new List<SpinEarnedCoupon> { earnedCoupon }
            };
            await spinRecordRepository.InsertAsync(record);
        } else {
            record.LastSpinUtc = now;
            record.TotalSpins++;
            record.EarnedCoupons.Add(earnedCoupon);
            await spinRecordRepository.UpdateAsync(record);
        }

        // Apply to cart — non-critical
        try {
            var customer = await customerService.GetCustomerById(request.CustomerId);
            if (customer != null) {
                var applied = customer.ApplyCouponCode(SystemCustomerFieldNames.DiscountCoupons, couponCode);
                await customerService.UpdateUserField(customer, SystemCustomerFieldNames.DiscountCoupons, applied);
                earnedCoupon.AppliedToCart = true;
                await spinRecordRepository.UpdateAsync(record);
            }
        } catch {
            // Reconciliation handles this on next cart load
        }

        return new SpinExecuteResult {
            Success = true,
            WinningSegmentIndex = segmentIndex,
            CouponCode = couponCode,
            DiscountLabel = segment.Label,
            NextSpinAt = now.AddHours(settings.CooldownHours)
        };
    }

    private static SpinSegment PickWeightedSegment(List<SpinSegment> segments)
    {
        var total = segments.Sum(s => s.ProbabilityWeight);
        var draw = Random.Shared.Next(0, total);
        var running = 0;
        foreach (var segment in segments) {
            running += segment.ProbabilityWeight;
            if (running > draw) return segment;
        }
        return segments[^1];
    }

    private static string GenerateAlphanumeric(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }

    private static SpinExecuteResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
