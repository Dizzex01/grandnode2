using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Data;
using Grand.Data.Tests.MongoDb;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Queries;

namespace Grand.Plugins.SpinWheel.Tests.Queries;

[TestClass]
public class GetSpinStateQueryHandlerTests
{
    private GetSpinStateQueryHandler _handler;
    private IRepository<CustomerSpinRecord> _spinRecordRepository;
    private Mock<ISettingService> _settingServiceMock;

    [TestInitialize]
    public void Init()
    {
        _spinRecordRepository = new MongoDBRepositoryTest<CustomerSpinRecord>();
        _settingServiceMock = new Mock<ISettingService>();

        _settingServiceMock
            .Setup(s => s.LoadSetting<SpinWheelSettings>(It.IsAny<string>()))
            .ReturnsAsync(new SpinWheelSettings {
                Enabled = true,
                CooldownHours = 24,
                Segments = new List<SpinSegment> {
                    new() { Id = "s1", Label = "10% Off", ProbabilityWeight = 50, Color = "#059669", DiscountPercent = 10, CouponPrefix = "SPIN10" },
                    new() { Id = "s2", Label = "20% Off", ProbabilityWeight = 50, Color = "#0891b2", DiscountPercent = 20, CouponPrefix = "SPIN20" }
                }
            });

        _handler = new GetSpinStateQueryHandler(_spinRecordRepository, _settingServiceMock.Object);
    }

    [TestMethod]
    public async Task Handle_NoSpinRecord_ReturnsCanSpinTrue()
    {
        var result = await _handler.Handle(
            new GetSpinStateQuery { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsTrue(result.CanSpin);
        Assert.IsNull(result.NextSpinAt);
        Assert.AreEqual(2, result.Segments.Count);
    }

    [TestMethod]
    public async Task Handle_SpinWithinCooldown_ReturnsCanSpinFalse()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord {
            CustomerId = "cust2",
            LastSpinUtc = DateTime.UtcNow.AddHours(-12),
            TotalSpins = 1
        });

        var result = await _handler.Handle(
            new GetSpinStateQuery { CustomerId = "cust2", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsFalse(result.CanSpin);
        Assert.IsNotNull(result.NextSpinAt);
    }

    [TestMethod]
    public async Task Handle_SpinAfterCooldown_ReturnsCanSpinTrue()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord {
            CustomerId = "cust3",
            LastSpinUtc = DateTime.UtcNow.AddHours(-48),
            TotalSpins = 1
        });

        var result = await _handler.Handle(
            new GetSpinStateQuery { CustomerId = "cust3", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsTrue(result.CanSpin);
    }

    [TestMethod]
    public async Task Handle_Disabled_IsEnabledFalse()
    {
        _settingServiceMock
            .Setup(s => s.LoadSetting<SpinWheelSettings>(It.IsAny<string>()))
            .ReturnsAsync(new SpinWheelSettings { Enabled = false, CooldownHours = 24, Segments = new List<SpinSegment>() });

        var result = await _handler.Handle(
            new GetSpinStateQuery { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        Assert.IsFalse(result.IsEnabled);
    }

    [TestMethod]
    public async Task Handle_SegmentsMappedCorrectly()
    {
        var result = await _handler.Handle(
            new GetSpinStateQuery { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        Assert.AreEqual(2, result.Segments.Count);
        Assert.AreEqual("s1", result.Segments[0].Id);
        Assert.AreEqual("10% Off", result.Segments[0].Label);
        Assert.AreEqual(50, result.Segments[0].ProbabilityWeight);
        Assert.AreEqual("#059669", result.Segments[0].Color);
    }
}
