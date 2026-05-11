using Grand.Business.Core.Interfaces.Customers;
using Grand.Data;
using Grand.Data.Tests.MongoDb;
using Grand.Domain.Customers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Services;

namespace Grand.Plugins.SpinWheel.Tests.Services;

[TestClass]
public class SpinCouponReconciliationServiceTests
{
    private SpinCouponReconciliationService _service;
    private IRepository<CustomerSpinRecord> _spinRecordRepository;
    private Mock<ICustomerService> _customerServiceMock;

    [TestInitialize]
    public void Init()
    {
        _spinRecordRepository = new MongoDBRepositoryTest<CustomerSpinRecord>();
        _customerServiceMock = new Mock<ICustomerService>();

        _customerServiceMock
            .Setup(c => c.GetCustomerById(It.IsAny<string>()))
            .ReturnsAsync(new Customer { Id = "cust1" });
        _customerServiceMock
            .Setup(c => c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _service = new SpinCouponReconciliationService(_spinRecordRepository, _customerServiceMock.Object);
    }

    [TestMethod]
    public async Task EnsureApplied_UnappliedCoupon_AppliesAndFlipsFlag()
    {
        var coupon = new SpinEarnedCoupon {
            CouponCode = "SPIN10-ABC123",
            DiscountLabel = "10% Off",
            EarnedAtUtc = DateTime.UtcNow.AddHours(-1),
            AppliedToCart = false
        };
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord {
            CustomerId = "cust1",
            TotalSpins = 1,
            EarnedCoupons = new List<SpinEarnedCoupon> { coupon }
        });

        await _service.EnsureApplied("cust1");

        var record = await _spinRecordRepository.GetOneAsync(r => r.CustomerId == "cust1");
        Assert.IsTrue(record!.EarnedCoupons[0].AppliedToCart);
        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), SystemCustomerFieldNames.DiscountCoupons, It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [TestMethod]
    public async Task EnsureApplied_AlreadyAppliedCoupon_DoesNotReapply()
    {
        var coupon = new SpinEarnedCoupon {
            CouponCode = "SPIN10-ABC123",
            DiscountLabel = "10% Off",
            EarnedAtUtc = DateTime.UtcNow.AddHours(-1),
            AppliedToCart = true
        };
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord {
            CustomerId = "cust1",
            TotalSpins = 1,
            EarnedCoupons = new List<SpinEarnedCoupon> { coupon }
        });

        await _service.EnsureApplied("cust1");

        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task EnsureApplied_NoRecord_DoesNotThrow()
    {
        await _service.EnsureApplied("ghost-customer");

        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
