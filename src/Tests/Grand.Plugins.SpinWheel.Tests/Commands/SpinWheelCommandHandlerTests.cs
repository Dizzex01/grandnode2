using Grand.Business.Core.Interfaces.Catalog.Discounts;
using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Business.Core.Interfaces.Customers;
using Grand.Data;
using Grand.Data.Tests.MongoDb;
using Grand.Domain.Customers;
using Grand.Domain.Discounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Promotion.SpinWheel.Commands;
using Promotion.SpinWheel.Domain;

namespace Grand.Plugins.SpinWheel.Tests.Commands;

[TestClass]
public class SpinWheelCommandHandlerTests
{
    private SpinWheelCommandHandler _handler;
    private IRepository<CustomerSpinRecord> _spinRecordRepository;
    private Mock<ISettingService> _settingServiceMock;
    private Mock<IDiscountService> _discountServiceMock;
    private Mock<ICustomerService> _customerServiceMock;
    private SpinWheelSettings _defaultSettings;

    [TestInitialize]
    public void Init()
    {
        _spinRecordRepository = new MongoDBRepositoryTest<CustomerSpinRecord>();
        _settingServiceMock = new Mock<ISettingService>();
        _discountServiceMock = new Mock<IDiscountService>();
        _customerServiceMock = new Mock<ICustomerService>();

        _defaultSettings = new SpinWheelSettings {
            Enabled = true,
            CooldownHours = 24,
            Segments = new List<SpinSegment> {
                new() { Id = "s1", Label = "5% Off",  DiscountPercent = 5,  ProbabilityWeight = 50, CouponPrefix = "SPIN5",  Color = "#f59e0b" },
                new() { Id = "s2", Label = "20% Off", DiscountPercent = 20, ProbabilityWeight = 50, CouponPrefix = "SPIN20", Color = "#0891b2" }
            }
        };

        _settingServiceMock
            .Setup(s => s.LoadSetting<SpinWheelSettings>(It.IsAny<string>()))
            .ReturnsAsync(_defaultSettings);

        _discountServiceMock
            .Setup(d => d.InsertDiscount(It.IsAny<Discount>()))
            .Returns(Task.CompletedTask);
        _discountServiceMock
            .Setup(d => d.InsertDiscountCoupon(It.IsAny<DiscountCoupon>()))
            .Returns(Task.CompletedTask);

        _customerServiceMock
            .Setup(c => c.GetCustomerById(It.IsAny<string>()))
            .ReturnsAsync(new Customer { Id = "cust1" });
        _customerServiceMock
            .Setup(c => c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _handler = new SpinWheelCommandHandler(
            _spinRecordRepository,
            _settingServiceMock.Object,
            _discountServiceMock.Object,
            _customerServiceMock.Object);
    }

    [TestMethod]
    public async Task Handle_FirstSpin_ReturnsSuccessAndPersistsRecord()
    {
        var result = await _handler.Handle(
            new SpinWheelCommand { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.CouponCode));

        var record = await _spinRecordRepository.GetOneAsync(r => r.CustomerId == "cust1");
        Assert.IsNotNull(record);
        Assert.AreEqual(1, record.TotalSpins);
        Assert.AreEqual(1, record.EarnedCoupons.Count);
    }

    [TestMethod]
    public async Task Handle_CouponCodeFormat_MatchesPrefixDashSixAlphanumeric()
    {
        var result = await _handler.Handle(
            new SpinWheelCommand { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        var parts = result.CouponCode.Split('-');
        Assert.AreEqual(2, parts.Length, $"Expected PREFIX-SUFFIX but got: {result.CouponCode}");
        Assert.AreEqual(6, parts[1].Length);
        Assert.IsTrue(parts[1].All(char.IsLetterOrDigit));
    }

    [TestMethod]
    public async Task Handle_WithinCooldown_ReturnsFailure()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord {
            CustomerId = "cust1",
            LastSpinUtc = DateTime.UtcNow.AddHours(-6),
            TotalSpins = 1
        });

        var result = await _handler.Handle(
            new SpinWheelCommand { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task Handle_WeightedRandom_DistributionApproximatesWeights()
    {
        var wins = new Dictionary<string, int> { ["s1"] = 0, ["s2"] = 0 };
        const int trials = 500;

        for (var i = 0; i < trials; i++) {
            await _spinRecordRepository.DeleteManyAsync(r => r.CustomerId == "distTest");
            var result = await _handler.Handle(
                new SpinWheelCommand { CustomerId = "distTest", StoreId = "store1" },
                CancellationToken.None);

            var segId = _defaultSettings.Segments[result.WinningSegmentIndex].Id;
            wins[segId]++;
        }

        Assert.IsTrue(wins["s1"] is > 175 and < 325, $"s1 wins: {wins["s1"]} — distribution may be broken");
        Assert.IsTrue(wins["s2"] is > 175 and < 325, $"s2 wins: {wins["s2"]} — distribution may be broken");
    }
}
