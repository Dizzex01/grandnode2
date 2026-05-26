# Spin the Wheel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a `Promotion.SpinWheel` GrandNode plugin that adds a `/spin` page where logged-in customers spin an SVG wheel to win a discount coupon, which is persisted to MongoDB immediately and auto-applied to their cart.

**Architecture:** Self-contained plugin under `src/Plugins/Promotion.SpinWheel/` — zero changes to Grand.Core, Grand.Business, or Grand.Web. Uses MediatR (command/query), `IRepository<CustomerSpinRecord>` for MongoDB, `IDiscountService` + customer extension methods for coupon creation/application, and a Vue component in `wwwroot/spinwheel.js` for the interactive wheel.

**Tech Stack:** .NET 10, C# 12, ASP.NET Core MVC (plugin pattern), MediatR, MongoDB via `IRepository<T>`, Vue 2.7 (inline script, no separate build step), MSTest + Moq for tests.

---

## File Map

| File | Purpose |
|---|---|
| `Promotion.SpinWheel.csproj` | Plugin project — references Grand.Data, Grand.Domain, Grand.Infrastructure, Grand.Business.Core |
| `SpinWheelPlugin.cs` | `IPlugin` / `BasePlugin` — Install/Uninstall |
| `EndpointProvider.cs` | Registers `/spin` and admin routes |
| `DependencyInjection.cs` | Registers `ISpinCouponReconciliationService` with DI |
| `Domain/SpinWheelSettings.cs` | `ISettings` — Enabled, CooldownHours, Segments list |
| `Domain/CustomerSpinRecord.cs` | MongoDB document — per-customer spin history + earned coupons |
| `Models/ConfigureModel.cs` | Admin form view model |
| `Models/SpinStateResult.cs` | `GetSpinStateQuery` response |
| `Models/SpinExecuteResult.cs` | `SpinWheelCommand` response |
| `Queries/GetSpinStateQuery.cs` | Query + handler — reads cooldown state and segment config |
| `Commands/SpinWheelCommand.cs` | Command + handler — weighted random, coupon creation, DB persist |
| `Services/SpinCouponReconciliationService.cs` | Applies unapplied earned coupons to customer on cart load |
| `Controllers/SpinWheelController.cs` | `GET /spin`, `POST /spin/execute` |
| `Areas/Admin/Controllers/SpinWheelConfigController.cs` | `GET/POST /Admin/SpinWheelConfig/Configure` |
| `Areas/Admin/Views/SpinWheel/Configure.cshtml` | Admin config Razor view |
| `Views/SpinWheel/Index.cshtml` | Customer `/spin` page |
| `wwwroot/spinwheel.js` | Vue 2 component — SVG wheel, hover tooltips, spin animation, result screen |
| `Tests/.../SpinWheelCommandHandlerTests.cs` | Unit tests for spin logic |
| `Tests/.../SpinCouponReconciliationServiceTests.cs` | Unit tests for reconciliation |

---

## Task 1: Plugin Project Scaffold

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj`
- Create: `src/Plugins/Promotion.SpinWheel/SpinWheelPlugin.cs`
- Create: `src/Plugins/Promotion.SpinWheel/EndpointProvider.cs`
- Create: `src/Plugins/Promotion.SpinWheel/DependencyInjection.cs`

- [ ] **Step 1: Create the .csproj**

```xml
<!-- src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <Import Project="..\..\Build\Grand.Common.props" />
  <PropertyGroup>
    <RootNamespace>Promotion.SpinWheel</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Core\Grand.Data\Grand.Data.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <ProjectReference Include="..\..\Core\Grand.Domain\Grand.Domain.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <ProjectReference Include="..\..\Core\Grand.Infrastructure\Grand.Infrastructure.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <ProjectReference Include="..\..\Business\Grand.Business.Core\Grand.Business.Core.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <ProjectReference Include="..\..\Web\Grand.Web.Common\Grand.Web.Common.csproj">
      <Private>false</Private>
      <ExcludeAssets>all</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
  <ItemGroup>
    <Content Include="logo.jpg">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create SpinWheelPlugin.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/SpinWheelPlugin.cs
using Grand.Infrastructure.Plugins;

namespace Promotion.SpinWheel;

public class SpinWheelPlugin(IPluginTranslateResource pluginTranslateResource)
    : BasePlugin, IPlugin
{
    public override async Task Install()
    {
        await pluginTranslateResource.AddOrUpdatePluginTranslateResource(
            "Plugins.Promotion.SpinWheel.Title", "Spin to Win");
        await base.Install();
    }

    public override async Task Uninstall()
    {
        await pluginTranslateResource.DeletePluginTranslationResource(
            "Plugins.Promotion.SpinWheel.Title");
        await base.Uninstall();
    }
}
```

- [ ] **Step 3: Create EndpointProvider.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/EndpointProvider.cs
using Grand.Infrastructure.Endpoints;
using Microsoft.AspNetCore.Routing;

namespace Promotion.SpinWheel;

public class EndpointProvider : IEndpointProvider
{
    public void RegisterEndpoint(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            "Plugin.Promotion.SpinWheel.Index",
            "spin",
            new { controller = "SpinWheel", action = "Index" });

        endpointRouteBuilder.MapControllerRoute(
            "Plugin.Promotion.SpinWheel.Execute",
            "spin/execute",
            new { controller = "SpinWheel", action = "Execute" });

        endpointRouteBuilder.MapControllerRoute(
            "Plugin.Promotion.SpinWheel.Admin.Configure",
            "Admin/SpinWheelConfig/Configure",
            new { controller = "SpinWheelConfig", action = "Configure", area = "Admin" });
    }

    public int Priority => 0;
}
```

- [ ] **Step 4: Create DependencyInjection.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/DependencyInjection.cs
using Grand.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Promotion.SpinWheel.Services;

namespace Promotion.SpinWheel;

public class StartupApplication : IStartupApplication
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration = null)
    {
        services.AddScoped<ISpinCouponReconciliationService, SpinCouponReconciliationService>();
    }

    public void Configure(IApplicationBuilder application, IWebHostEnvironment webHostEnvironment) { }

    public int Priority => 100;
    public bool BeforeConfigure => false;
}
```

- [ ] **Step 5: Add a placeholder 50×50 `logo.jpg`**

Copy any existing plugin logo, e.g.:
```
cp src/Plugins/DiscountRules.Standard/logo.jpg src/Plugins/Promotion.SpinWheel/logo.jpg
```

- [ ] **Step 6: Add the plugin to the solution**

In Visual Studio or via CLI:
```
dotnet sln grandnode2.sln add src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj
```

- [ ] **Step 7: Verify the project builds**

```
dotnet build src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 8: Commit**

```
git add src/Plugins/Promotion.SpinWheel/
git commit -m "feat(spin-wheel): scaffold plugin project"
```

---

## Task 2: Domain Model

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Domain/SpinWheelSettings.cs`
- Create: `src/Plugins/Promotion.SpinWheel/Domain/CustomerSpinRecord.cs`

- [ ] **Step 1: Create SpinWheelSettings.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Domain/SpinWheelSettings.cs
using Grand.Domain.Configuration;

namespace Promotion.SpinWheel.Domain;

public class SpinWheelSettings : ISettings
{
    public bool Enabled { get; set; }
    public int CooldownHours { get; set; } = 72;
    public List<SpinSegment> Segments { get; set; } = new();
}

public class SpinSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public int ProbabilityWeight { get; set; } = 10;
    public string Color { get; set; } = "#7c3aed";
    public string CouponPrefix { get; set; } = "SPIN";
}
```

- [ ] **Step 2: Create CustomerSpinRecord.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Domain/CustomerSpinRecord.cs
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
```

- [ ] **Step 3: Verify build**

```
dotnet build src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj
```

- [ ] **Step 4: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Domain/
git commit -m "feat(spin-wheel): add domain model (SpinWheelSettings, CustomerSpinRecord)"
```

---

## Task 3: Result Models and Admin View Model

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Models/SpinStateResult.cs`
- Create: `src/Plugins/Promotion.SpinWheel/Models/SpinExecuteResult.cs`
- Create: `src/Plugins/Promotion.SpinWheel/Models/ConfigureModel.cs`

- [ ] **Step 1: Create SpinStateResult.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Models/SpinStateResult.cs
namespace Promotion.SpinWheel.Models;

public class SpinStateResult
{
    public bool CanSpin { get; set; }
    public DateTime? NextSpinAt { get; set; }
    public bool IsEnabled { get; set; }
    public List<SegmentDto> Segments { get; set; } = new();
}

public class SegmentDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ProbabilityWeight { get; set; }
    public string Color { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create SpinExecuteResult.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Models/SpinExecuteResult.cs
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
```

- [ ] **Step 3: Create ConfigureModel.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Models/ConfigureModel.cs
namespace Promotion.SpinWheel.Models;

public class ConfigureModel
{
    public bool Enabled { get; set; }
    public int CooldownHours { get; set; } = 72;
    public List<SegmentConfigModel> Segments { get; set; } = new();
}

public class SegmentConfigModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public int ProbabilityWeight { get; set; } = 10;
    public string Color { get; set; } = "#7c3aed";
    public string CouponPrefix { get; set; } = "SPIN";
}
```

- [ ] **Step 4: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Models/
git commit -m "feat(spin-wheel): add result and view models"
```

---

## Task 4: GetSpinStateQuery (Read Side) with Tests

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Queries/GetSpinStateQuery.cs`
- Create: `src/Tests/Grand.Plugins.SpinWheel.Tests/Grand.Plugins.SpinWheel.Tests.csproj`
- Create: `src/Tests/Grand.Plugins.SpinWheel.Tests/Queries/GetSpinStateQueryHandlerTests.cs`

- [ ] **Step 1: Create the test project**

```xml
<!-- src/Tests/Grand.Plugins.SpinWheel.Tests/Grand.Plugins.SpinWheel.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\Grand.Common.Tests.props" />
  <ItemGroup>
    <ProjectReference Include="..\..\..\Plugins\Promotion.SpinWheel\Promotion.SpinWheel.csproj" />
    <ProjectReference Include="..\MedHelper\MedHelper.csproj" />
  </ItemGroup>
</Project>
```

> **Note:** Check `src/Tests/` for the exact props filename and MedHelper reference used by other test projects — copy that pattern exactly.

- [ ] **Step 2: Write the failing tests**

```csharp
// src/Tests/Grand.Plugins.SpinWheel.Tests/Queries/GetSpinStateQueryHandlerTests.cs
using Grand.Data.Tests.MongoDb;
using Grand.Domain.Configuration;
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
            .ReturnsAsync(new SpinWheelSettings
            {
                Enabled = true,
                CooldownHours = 24,
                Segments = new List<SpinSegment>
                {
                    new() { Id = "s1", Label = "10% Off", ProbabilityWeight = 50, Color = "#059669", DiscountPercent = 10, CouponPrefix = "SPIN10" },
                    new() { Id = "s2", Label = "20% Off", ProbabilityWeight = 50, Color = "#0891b2", DiscountPercent = 20, CouponPrefix = "SPIN20" }
                }
            });

        _handler = new GetSpinStateQueryHandler(_spinRecordRepository, _settingServiceMock.Object);
    }

    [TestMethod]
    public async Task Handle_NoSpinRecord_ReturnsCanSpinTrue()
    {
        var result = await _handler.Handle(new GetSpinStateQuery { CustomerId = "cust1", StoreId = "store1" }, CancellationToken.None);

        Assert.IsTrue(result.CanSpin);
        Assert.IsNull(result.NextSpinAt);
        Assert.AreEqual(2, result.Segments.Count);
    }

    [TestMethod]
    public async Task Handle_SpinWithinCooldown_ReturnsCanSpinFalse()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord
        {
            CustomerId = "cust2",
            LastSpinUtc = DateTime.UtcNow.AddHours(-12),
            TotalSpins = 1
        });

        var result = await _handler.Handle(new GetSpinStateQuery { CustomerId = "cust2", StoreId = "store1" }, CancellationToken.None);

        Assert.IsFalse(result.CanSpin);
        Assert.IsNotNull(result.NextSpinAt);
    }

    [TestMethod]
    public async Task Handle_SpinAfterCooldown_ReturnsCanSpinTrue()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord
        {
            CustomerId = "cust3",
            LastSpinUtc = DateTime.UtcNow.AddHours(-48),
            TotalSpins = 1
        });

        var result = await _handler.Handle(new GetSpinStateQuery { CustomerId = "cust3", StoreId = "store1" }, CancellationToken.None);

        Assert.IsTrue(result.CanSpin);
    }
}
```

- [ ] **Step 3: Run — verify they FAIL (handler not implemented yet)**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "GetSpinStateQueryHandlerTests"
```
Expected: compile error or runtime failure — handler class does not exist.

- [ ] **Step 4: Create GetSpinStateQuery.cs with handler**

```csharp
// src/Plugins/Promotion.SpinWheel/Queries/GetSpinStateQuery.cs
using Grand.Data;
using Grand.Domain.Configuration;
using MediatR;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Models;

namespace Promotion.SpinWheel.Queries;

public class GetSpinStateQuery : IRequest<SpinStateResult>
{
    public string CustomerId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
}

public class GetSpinStateQueryHandler(
    IRepository<CustomerSpinRecord> spinRecordRepository,
    ISettingService settingService)
    : IRequestHandler<GetSpinStateQuery, SpinStateResult>
{
    public async Task<SpinStateResult> Handle(GetSpinStateQuery request, CancellationToken cancellationToken)
    {
        var settings = await settingService.LoadSetting<SpinWheelSettings>(request.StoreId);
        var record = await spinRecordRepository.GetOneAsync(r => r.CustomerId == request.CustomerId);

        var now = DateTime.UtcNow;
        var nextSpinAt = record != null
            ? record.LastSpinUtc.AddHours(settings.CooldownHours)
            : (DateTime?)null;
        var canSpin = nextSpinAt == null || nextSpinAt <= now;

        return new SpinStateResult
        {
            IsEnabled = settings.Enabled,
            CanSpin = canSpin,
            NextSpinAt = canSpin ? null : nextSpinAt,
            Segments = settings.Segments.Select(s => new SegmentDto
            {
                Id = s.Id,
                Label = s.Label,
                ProbabilityWeight = s.ProbabilityWeight,
                Color = s.Color
            }).ToList()
        };
    }
}
```

- [ ] **Step 5: Run — verify tests pass**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "GetSpinStateQueryHandlerTests"
```
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Queries/ src/Tests/Grand.Plugins.SpinWheel.Tests/
git commit -m "feat(spin-wheel): add GetSpinStateQuery with tests"
```

---

## Task 5: SpinWheelCommand (Write Side) with Tests

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Commands/SpinWheelCommand.cs`
- Create: `src/Tests/Grand.Plugins.SpinWheel.Tests/Commands/SpinWheelCommandHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Tests/Grand.Plugins.SpinWheel.Tests/Commands/SpinWheelCommandHandlerTests.cs
using Grand.Data.Tests.MongoDb;
using Grand.Domain.Configuration;
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

        _defaultSettings = new SpinWheelSettings
        {
            Enabled = true,
            CooldownHours = 24,
            Segments = new List<SpinSegment>
            {
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
            .Setup(c => c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>()))
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
        Assert.IsFalse(record.EarnedCoupons[0].AppliedToCart);
    }

    [TestMethod]
    public async Task Handle_CouponCodeFormat_MatchesPrefixDashSixAlphanumeric()
    {
        var result = await _handler.Handle(
            new SpinWheelCommand { CustomerId = "cust1", StoreId = "store1" },
            CancellationToken.None);

        // Code is PREFIX-XXXXXX where X is alphanumeric
        var parts = result.CouponCode.Split('-');
        Assert.AreEqual(2, parts.Length, $"Expected PREFIX-SUFFIX but got: {result.CouponCode}");
        Assert.AreEqual(6, parts[1].Length);
        Assert.IsTrue(parts[1].All(char.IsLetterOrDigit));
    }

    [TestMethod]
    public async Task Handle_WithinCooldown_ReturnsFailure()
    {
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord
        {
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
        // With equal weights (50/50), each segment should win ~50% over many trials
        var wins = new Dictionary<string, int> { ["s1"] = 0, ["s2"] = 0 };
        const int trials = 500;

        for (var i = 0; i < trials; i++)
        {
            await _spinRecordRepository.DeleteAsync(r => r.CustomerId == "distTest");
            var result = await _handler.Handle(
                new SpinWheelCommand { CustomerId = "distTest", StoreId = "store1" },
                CancellationToken.None);

            var segIndex = result.WinningSegmentIndex;
            var segId = _defaultSettings.Segments[segIndex].Id;
            wins[segId]++;
        }

        // Each should win between 35%–65% (allowing wide margin for randomness)
        Assert.IsTrue(wins["s1"] is > 175 and < 325, $"s1 wins: {wins["s1"]} — distribution may be broken");
        Assert.IsTrue(wins["s2"] is > 175 and < 325, $"s2 wins: {wins["s2"]} — distribution may be broken");
    }
}
```

- [ ] **Step 2: Run — verify they FAIL**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "SpinWheelCommandHandlerTests"
```
Expected: compile error — `SpinWheelCommand` and `SpinWheelCommandHandler` don't exist.

- [ ] **Step 3: Create SpinWheelCommand.cs with handler**

```csharp
// src/Plugins/Promotion.SpinWheel/Commands/SpinWheelCommand.cs
using Grand.Business.Core.Interfaces.Catalog.Discounts;
using Grand.Business.Core.Interfaces.Customers;
using Grand.Data;
using Grand.Domain.Configuration;
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

        if (record != null)
        {
            var nextAllowed = record.LastSpinUtc.AddHours(settings.CooldownHours);
            if (now < nextAllowed)
                return Fail($"Cooldown active. Next spin at {nextAllowed:u}.");
        }

        var segment = PickWeightedSegment(settings.Segments);
        var segmentIndex = settings.Segments.IndexOf(segment);
        var couponCode = $"{segment.CouponPrefix}-{GenerateAlphanumeric(6)}";

        // Create discount + coupon in DB
        var discount = new Discount
        {
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

        var coupon = new DiscountCoupon
        {
            DiscountId = discount.Id,
            CouponCode = couponCode,
            Used = false
        };
        await discountService.InsertDiscountCoupon(coupon);

        // Persist spin record first (durable before cart apply)
        var earnedCoupon = new SpinEarnedCoupon
        {
            CouponCode = couponCode,
            DiscountLabel = segment.Label,
            EarnedAtUtc = now,
            AppliedToCart = false
        };

        if (record == null)
        {
            record = new CustomerSpinRecord
            {
                CustomerId = request.CustomerId,
                LastSpinUtc = now,
                TotalSpins = 1,
                EarnedCoupons = new List<SpinEarnedCoupon> { earnedCoupon }
            };
            await spinRecordRepository.InsertAsync(record);
        }
        else
        {
            record.LastSpinUtc = now;
            record.TotalSpins++;
            record.EarnedCoupons.Add(earnedCoupon);
            await spinRecordRepository.UpdateAsync(record);
        }

        // Apply to customer's cart — non-critical
        try
        {
            var customer = await customerService.GetCustomerById(request.CustomerId);
            if (customer != null)
            {
                var applied = customer.ApplyCouponCode(SystemCustomerFieldNames.DiscountCoupons, couponCode);
                await customerService.UpdateUserField(customer, SystemCustomerFieldNames.DiscountCoupons, applied);

                // Mark as applied in the record
                earnedCoupon.AppliedToCart = true;
                await spinRecordRepository.UpdateAsync(record);
            }
        }
        catch
        {
            // Silently swallow — reconciliation handles it on next cart load
        }

        return new SpinExecuteResult
        {
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
        foreach (var segment in segments)
        {
            running += segment.ProbabilityWeight;
            if (running > draw)
                return segment;
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
```

- [ ] **Step 4: Run — verify tests pass**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "SpinWheelCommandHandlerTests"
```
Expected: 4 tests pass. (Distribution test may occasionally flap — run again if it does, as the margin is intentionally wide.)

- [ ] **Step 5: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Commands/ src/Tests/Grand.Plugins.SpinWheel.Tests/Commands/
git commit -m "feat(spin-wheel): add SpinWheelCommand with weighted random + coupon generation"
```

---

## Task 6: SpinCouponReconciliationService with Tests

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Services/ISpinCouponReconciliationService.cs`
- Create: `src/Plugins/Promotion.SpinWheel/Services/SpinCouponReconciliationService.cs`
- Create: `src/Tests/Grand.Plugins.SpinWheel.Tests/Services/SpinCouponReconciliationServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// src/Tests/Grand.Plugins.SpinWheel.Tests/Services/SpinCouponReconciliationServiceTests.cs
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
            .Setup(c => c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _service = new SpinCouponReconciliationService(_spinRecordRepository, _customerServiceMock.Object);
    }

    [TestMethod]
    public async Task EnsureApplied_UnappliedCoupon_AppliesAndFlipsFlag()
    {
        var coupon = new SpinEarnedCoupon
        {
            CouponCode = "SPIN10-ABC123",
            DiscountLabel = "10% Off",
            EarnedAtUtc = DateTime.UtcNow.AddHours(-1),
            AppliedToCart = false
        };
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord
        {
            CustomerId = "cust1",
            TotalSpins = 1,
            EarnedCoupons = new List<SpinEarnedCoupon> { coupon }
        });

        await _service.EnsureApplied("cust1");

        var record = await _spinRecordRepository.GetOneAsync(r => r.CustomerId == "cust1");
        Assert.IsTrue(record!.EarnedCoupons[0].AppliedToCart);
        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), SystemCustomerFieldNames.DiscountCoupons, It.IsAny<string>()),
            Times.Once);
    }

    [TestMethod]
    public async Task EnsureApplied_AlreadyAppliedCoupon_DoesNotReapply()
    {
        var coupon = new SpinEarnedCoupon
        {
            CouponCode = "SPIN10-ABC123",
            DiscountLabel = "10% Off",
            EarnedAtUtc = DateTime.UtcNow.AddHours(-1),
            AppliedToCart = true   // already applied
        };
        await _spinRecordRepository.InsertAsync(new CustomerSpinRecord
        {
            CustomerId = "cust1",
            TotalSpins = 1,
            EarnedCoupons = new List<SpinEarnedCoupon> { coupon }
        });

        await _service.EnsureApplied("cust1");

        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task EnsureApplied_NoRecord_DoesNotThrow()
    {
        // No CustomerSpinRecord for this customer — should be a no-op
        await _service.EnsureApplied("ghost-customer");

        _customerServiceMock.Verify(c =>
            c.UpdateUserField(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run — verify they FAIL**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "SpinCouponReconciliationServiceTests"
```

- [ ] **Step 3: Create the interface and service**

```csharp
// src/Plugins/Promotion.SpinWheel/Services/ISpinCouponReconciliationService.cs
namespace Promotion.SpinWheel.Services;

public interface ISpinCouponReconciliationService
{
    Task EnsureApplied(string customerId);
}
```

```csharp
// src/Plugins/Promotion.SpinWheel/Services/SpinCouponReconciliationService.cs
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
        foreach (var earned in unapplied)
        {
            try
            {
                var applied = customer.ApplyCouponCode(SystemCustomerFieldNames.DiscountCoupons, earned.CouponCode);
                await customerService.UpdateUserField(customer, SystemCustomerFieldNames.DiscountCoupons, applied);
                earned.AppliedToCart = true;
                changed = true;
            }
            catch
            {
                // Silent — will retry on next cart load
            }
        }

        if (changed)
            await spinRecordRepository.UpdateAsync(record);
    }
}
```

- [ ] **Step 4: Run — verify tests pass**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/ --filter "SpinCouponReconciliationServiceTests"
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Services/ src/Tests/Grand.Plugins.SpinWheel.Tests/Services/
git commit -m "feat(spin-wheel): add SpinCouponReconciliationService with tests"
```

---

## Task 7: SpinWheelController (Customer-Facing Routes)

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Controllers/SpinWheelController.cs`

- [ ] **Step 1: Create SpinWheelController.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Controllers/SpinWheelController.cs
using Grand.Web.Common.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Promotion.SpinWheel.Commands;
using Promotion.SpinWheel.Queries;
using Promotion.SpinWheel.Services;

namespace Promotion.SpinWheel.Controllers;

[Authorize]
public class SpinWheelController(
    IMediator mediator,
    ISpinCouponReconciliationService reconciliationService,
    IContextAccessor contextAccessor)
    : BasePublicController
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var customerId = contextAccessor.WorkContext.CurrentCustomer.Id;
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;

        var state = await mediator.Send(new GetSpinStateQuery
        {
            CustomerId = customerId,
            StoreId = storeId
        });

        if (!state.IsEnabled)
            return RedirectToRoute("HomePage");

        // Reconcile any unapplied coupons from previous sessions
        await reconciliationService.EnsureApplied(customerId);

        return View(state);
    }

    [HttpPost]
    public async Task<IActionResult> Execute()
    {
        var customerId = contextAccessor.WorkContext.CurrentCustomer.Id;
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;

        var result = await mediator.Send(new SpinWheelCommand
        {
            CustomerId = customerId,
            StoreId = storeId
        });

        return Json(result);
    }
}
```

- [ ] **Step 2: Verify build**

```
dotnet build src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj
```

- [ ] **Step 3: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Controllers/SpinWheelController.cs
git commit -m "feat(spin-wheel): add customer SpinWheelController"
```

---

## Task 8: Admin Configuration Controller and View

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Areas/Admin/Controllers/SpinWheelConfigController.cs`
- Create: `src/Plugins/Promotion.SpinWheel/Areas/Admin/Views/SpinWheel/Configure.cshtml`

- [ ] **Step 1: Create SpinWheelConfigController.cs**

```csharp
// src/Plugins/Promotion.SpinWheel/Areas/Admin/Controllers/SpinWheelConfigController.cs
using Grand.Domain.Configuration;
using Grand.Web.Common.Controllers;
using Microsoft.AspNetCore.Mvc;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Models;

namespace Promotion.SpinWheel.Areas.Admin.Controllers;

[Area("Admin")]
public class SpinWheelConfigController(ISettingService settingService, IContextAccessor contextAccessor)
    : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Configure()
    {
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;
        var settings = await settingService.LoadSetting<SpinWheelSettings>(storeId);

        var model = new ConfigureModel
        {
            Enabled = settings.Enabled,
            CooldownHours = settings.CooldownHours,
            Segments = settings.Segments.Select(s => new SegmentConfigModel
            {
                Id = s.Id,
                Label = s.Label,
                DiscountPercent = s.DiscountPercent,
                ProbabilityWeight = s.ProbabilityWeight,
                Color = s.Color,
                CouponPrefix = s.CouponPrefix
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Configure(ConfigureModel model)
    {
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;
        var settings = new SpinWheelSettings
        {
            Enabled = model.Enabled,
            CooldownHours = model.CooldownHours,
            Segments = model.Segments.Select(s => new SpinSegment
            {
                Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
                Label = s.Label,
                DiscountPercent = s.DiscountPercent,
                ProbabilityWeight = s.ProbabilityWeight,
                Color = s.Color,
                CouponPrefix = s.CouponPrefix.ToUpperInvariant()
            }).ToList()
        };

        await settingService.SaveSetting(settings, storeId);

        TempData["success"] = "Spin Wheel configuration saved.";
        return RedirectToAction(nameof(Configure));
    }
}
```

- [ ] **Step 2: Create Configure.cshtml**

```cshtml
@* src/Plugins/Promotion.SpinWheel/Areas/Admin/Views/SpinWheel/Configure.cshtml *@
@model Promotion.SpinWheel.Models.ConfigureModel
@{
    Layout = "_ConfigurePlugin";
    ViewBag.Title = "Spin Wheel Configuration";
}

<form asp-action="Configure" method="post">
    <div class="card">
        <div class="card-header">
            <h5>General Settings</h5>
        </div>
        <div class="card-body">
            <div class="form-group row">
                <admin-label asp-for="Enabled" />
                <div class="col-md-9">
                    <admin-input asp-for="Enabled" />
                </div>
            </div>
            <div class="form-group row">
                <admin-label asp-for="CooldownHours" />
                <div class="col-md-9">
                    <admin-input asp-for="CooldownHours" />
                    <span class="hint">Hours between spins per customer. E.g. 72 = once every 3 days.</span>
                </div>
            </div>
        </div>
    </div>

    <div class="card mt-3">
        <div class="card-header d-flex justify-content-between align-items-center">
            <h5>Wheel Segments</h5>
            <button type="button" class="btn btn-sm btn-primary" onclick="addSegment()">+ Add Segment</button>
        </div>
        <div class="card-body">
            <table class="table table-sm" id="segments-table">
                <thead>
                    <tr>
                        <th>Color</th>
                        <th>Label</th>
                        <th>Discount %</th>
                        <th>Weight</th>
                        <th>Coupon Prefix</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody id="segments-body">
                    @for (var i = 0; i < Model.Segments.Count; i++)
                    {
                        <tr>
                            <td><input type="color" name="Segments[@i].Color" value="@Model.Segments[i].Color" /></td>
                            <td><input class="form-control form-control-sm" name="Segments[@i].Label" value="@Model.Segments[i].Label" /></td>
                            <td><input type="number" step="0.01" class="form-control form-control-sm" name="Segments[@i].DiscountPercent" value="@Model.Segments[i].DiscountPercent" /></td>
                            <td><input type="number" class="form-control form-control-sm" name="Segments[@i].ProbabilityWeight" value="@Model.Segments[i].ProbabilityWeight" /></td>
                            <td><input class="form-control form-control-sm" name="Segments[@i].CouponPrefix" value="@Model.Segments[i].CouponPrefix" /></td>
                            <td><button type="button" class="btn btn-sm btn-danger" onclick="this.closest('tr').remove(); reindex()">Remove</button></td>
                            <input type="hidden" name="Segments[@i].Id" value="@Model.Segments[i].Id" />
                        </tr>
                    }
                </tbody>
            </table>
            <div id="weight-summary" class="text-muted small mt-2"></div>
        </div>
    </div>

    <div class="mt-3">
        <button type="submit" class="btn btn-primary">Save</button>
    </div>
</form>

<script>
let segIndex = @Model.Segments.Count;

function addSegment() {
    const i = segIndex++;
    const row = `<tr>
        <td><input type="color" name="Segments[${i}].Color" value="#7c3aed" /></td>
        <td><input class="form-control form-control-sm" name="Segments[${i}].Label" value="" /></td>
        <td><input type="number" step="0.01" class="form-control form-control-sm" name="Segments[${i}].DiscountPercent" value="10" /></td>
        <td><input type="number" class="form-control form-control-sm" name="Segments[${i}].ProbabilityWeight" value="10" /></td>
        <td><input class="form-control form-control-sm" name="Segments[${i}].CouponPrefix" value="SPIN" /></td>
        <td><button type="button" class="btn btn-sm btn-danger" onclick="this.closest('tr').remove(); reindex()">Remove</button></td>
        <input type="hidden" name="Segments[${i}].Id" value="" />
    </tr>`;
    document.getElementById('segments-body').insertAdjacentHTML('beforeend', row);
    updateWeightSummary();
}

function reindex() {
    const rows = document.querySelectorAll('#segments-body tr');
    rows.forEach((row, i) => {
        row.querySelectorAll('input').forEach(input => {
            input.name = input.name.replace(/\[\d+\]/, `[${i}]`);
        });
    });
    segIndex = rows.length;
    updateWeightSummary();
}

function updateWeightSummary() {
    const weights = [...document.querySelectorAll('[name$="].ProbabilityWeight"]')]
        .map(i => parseInt(i.value) || 0);
    const total = weights.reduce((a, b) => a + b, 0);
    document.getElementById('weight-summary').textContent =
        total > 0 ? `Total weight: ${total}` : 'Add segments to configure the wheel.';
}

document.addEventListener('input', updateWeightSummary);
updateWeightSummary();
</script>
```

- [ ] **Step 3: Verify build**

```
dotnet build src/Plugins/Promotion.SpinWheel/Promotion.SpinWheel.csproj
```

- [ ] **Step 4: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Areas/
git commit -m "feat(spin-wheel): add admin configuration controller and view"
```

---

## Task 9: Customer-Facing View (Index.cshtml)

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/Views/SpinWheel/Index.cshtml`

- [ ] **Step 1: Create Index.cshtml**

```cshtml
@* src/Plugins/Promotion.SpinWheel/Views/SpinWheel/Index.cshtml *@
@model Promotion.SpinWheel.Models.SpinStateResult
@{
    Layout = "_SingleColumn";
    ViewBag.Title = "Spin to Win";
    var segmentsJson = System.Text.Json.JsonSerializer.Serialize(Model.Segments);
    var canSpin = Model.CanSpin;
    var nextSpinAt = Model.NextSpinAt.HasValue
        ? Model.NextSpinAt.Value.ToString("o")
        : null;
}

<div id="spin-wheel-app">
    <spin-wheel
        :segments='@Html.Raw(segmentsJson)'
        :can-spin="@canSpin.ToString().ToLower()"
        next-spin-at="@nextSpinAt"
        execute-url="@Url.Action("Execute", "SpinWheel")"
        cart-url="@Url.RouteUrl("ShoppingCart")"
        antiforgery-token="@Html.AntiForgeryToken().ToString()">
    </spin-wheel>
</div>

@section scripts {
    <script src="~/Plugins/Promotion.SpinWheel/spinwheel.js"></script>
}
```

- [ ] **Step 2: Commit**

```
git add src/Plugins/Promotion.SpinWheel/Views/
git commit -m "feat(spin-wheel): add customer Index.cshtml view"
```

---

## Task 10: Vue Wheel Component (spinwheel.js)

**Files:**
- Create: `src/Plugins/Promotion.SpinWheel/wwwroot/spinwheel.js`

- [ ] **Step 1: Create spinwheel.js**

```javascript
// src/Plugins/Promotion.SpinWheel/wwwroot/spinwheel.js
Vue.component('spin-wheel', {
    props: {
        segments:        { type: Array,   required: true },
        canSpin:         { type: Boolean, default: true },
        nextSpinAt:      { type: String,  default: null },
        executeUrl:      { type: String,  required: true },
        cartUrl:         { type: String,  required: true },
        antiforgeryToken:{ type: String,  default: '' }
    },
    data() {
        return {
            spinning: false,
            result: null,
            rotation: 0,
            hoveredIndex: null,
            countdown: '',
            cx: 150, cy: 150, r: 140
        };
    },
    computed: {
        totalWeight() {
            return this.segments.reduce((sum, s) => sum + s.probabilityWeight, 0);
        },
        paths() {
            let startAngle = 0;
            return this.segments.map((seg, i) => {
                const angle = (seg.probabilityWeight / this.totalWeight) * 360;
                const path = this.describeArc(startAngle, startAngle + angle);
                const midAngle = startAngle + angle / 2;
                startAngle += angle;
                return { ...seg, path, midAngle, index: i };
            });
        }
    },
    mounted() {
        if (!this.canSpin && this.nextSpinAt)
            this.startCountdown(new Date(this.nextSpinAt));
    },
    methods: {
        toRad(deg) { return deg * Math.PI / 180; },
        pointOnCircle(angleDeg) {
            const rad = this.toRad(angleDeg - 90); // 0° = top (12 o'clock)
            return {
                x: this.cx + this.r * Math.cos(rad),
                y: this.cy + this.r * Math.sin(rad)
            };
        },
        describeArc(startDeg, endDeg) {
            const s = this.pointOnCircle(startDeg);
            const e = this.pointOnCircle(endDeg);
            const large = (endDeg - startDeg) > 180 ? 1 : 0;
            return `M${this.cx},${this.cy} L${s.x.toFixed(2)},${s.y.toFixed(2)} A${this.r},${this.r} 0 ${large},1 ${e.x.toFixed(2)},${e.y.toFixed(2)} Z`;
        },
        segmentStyle(index) {
            if (this.spinning) return {};
            if (this.hoveredIndex === null) return {};
            return this.hoveredIndex === index
                ? { filter: 'drop-shadow(0 0 8px rgba(0,0,0,0.6))' }
                : { opacity: '0.4' };
        },
        tooltipPos(midAngle) {
            const rad = this.toRad(midAngle - 90);
            const tr = this.r * 0.65;
            return {
                x: this.cx + tr * Math.cos(rad),
                y: this.cy + tr * Math.sin(rad)
            };
        },
        async spin() {
            if (this.spinning || !this.canSpin) return;
            this.spinning = true;
            this.result = null;

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

            try {
                const resp = await fetch(this.executeUrl, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token }
                });
                const data = await resp.json();

                if (!data.success) {
                    alert(data.errorMessage || 'Could not spin. Please try again.');
                    this.spinning = false;
                    return;
                }

                // Calculate rotation to land on winning segment
                const segAngle = (this.segments[data.winningSegmentIndex].probabilityWeight / this.totalWeight) * 360;
                let startAngle = 0;
                for (let i = 0; i < data.winningSegmentIndex; i++)
                    startAngle += (this.segments[i].probabilityWeight / this.totalWeight) * 360;
                const midAngle = startAngle + segAngle / 2;
                // Pointer is at top (0°). Rotate so midAngle aligns with top.
                const extraSpins = 6;
                this.rotation += (360 * extraSpins) + (360 - midAngle) - (this.rotation % 360);

                setTimeout(() => {
                    this.result = data;
                    this.spinning = false;
                    this.startCountdown(new Date(data.nextSpinAt));
                }, 3200);

            } catch (e) {
                alert('An error occurred. Please refresh and try again.');
                this.spinning = false;
            }
        },
        startCountdown(until) {
            const tick = () => {
                const diff = until - new Date();
                if (diff <= 0) { this.countdown = ''; return; }
                const h = Math.floor(diff / 3600000);
                const m = Math.floor((diff % 3600000) / 60000);
                this.countdown = `${h}h ${m}m`;
                setTimeout(tick, 60000);
            };
            tick();
        },
        copyCode() {
            navigator.clipboard.writeText(this.result.couponCode)
                .then(() => alert('Copied!'))
                .catch(() => {});
        }
    },
    template: `
    <div style="text-align:center;padding:40px 20px;max-width:500px;margin:0 auto;">
        <h1 style="margin-bottom:4px;">Spin to Win!</h1>
        <p style="color:#888;margin-bottom:24px;">
            Spin the wheel for a discount coupon.
            <span v-if="countdown">Next spin in: <strong>{{ countdown }}</strong></span>
        </p>

        <!-- Result screen -->
        <div v-if="result" style="margin-bottom:32px;">
            <div style="font-size:48px;">🎉</div>
            <h2 style="color:#059669;">You won {{ result.discountLabel }}!</h2>
            <p style="color:#888;">Applied to your cart automatically.</p>
            <div style="border:2px dashed #059669;border-radius:8px;padding:16px;margin:16px auto;max-width:280px;background:#f0fdf4;">
                <div style="font-size:11px;color:#888;text-transform:uppercase;letter-spacing:1px;">Your Coupon Code</div>
                <div style="font-size:24px;font-weight:bold;font-family:monospace;letter-spacing:3px;color:#059669;margin:6px 0;">
                    {{ result.couponCode }}
                </div>
                <button @click="copyCode" style="font-size:12px;background:none;border:1px solid #059669;color:#059669;padding:4px 12px;border-radius:4px;cursor:pointer;">
                    Copy Code
                </button>
            </div>
            <div style="display:flex;gap:12px;justify-content:center;margin-top:16px;">
                <a :href="cartUrl" style="padding:10px 20px;background:#7c3aed;color:#fff;border-radius:6px;text-decoration:none;">🛒 Go to Cart</a>
                <a href="/" style="padding:10px 20px;border:1px solid #ccc;border-radius:6px;color:#555;text-decoration:none;">Continue Shopping</a>
            </div>
        </div>

        <!-- Wheel -->
        <div style="position:relative;display:inline-block;">
            <div style="position:absolute;top:-22px;left:50%;transform:translateX(-50%);font-size:24px;z-index:10;line-height:1;">▼</div>
            <svg :width="cx*2" :height="cy*2"
                 :style="{ transition: spinning ? 'transform 3.2s cubic-bezier(0.17,0.67,0.12,0.99)' : 'none', transform: 'rotate(' + rotation + 'deg)', transformOrigin: cx+'px '+cy+'px' }">
                <path v-for="seg in paths" :key="seg.index"
                      :d="seg.path"
                      :fill="seg.color"
                      stroke="#fff" stroke-width="2"
                      :style="segmentStyle(seg.index)"
                      style="cursor:pointer;transition:opacity 0.2s,filter 0.2s;"
                      @mouseenter="!spinning && (hoveredIndex = seg.index)"
                      @mouseleave="hoveredIndex = null">
                </path>
                <circle :cx="cx" :cy="cy" r="18" fill="#fff" stroke="#333" stroke-width="3"/>
            </svg>
            <!-- Hover tooltip -->
            <div v-if="hoveredIndex !== null && !spinning"
                 style="position:absolute;background:#1e293b;color:#fff;border-radius:6px;padding:6px 10px;font-size:12px;pointer-events:none;white-space:nowrap;z-index:20;top:50%;left:50%;transform:translate(-50%,-50%);">
                {{ segments[hoveredIndex].label }}
            </div>
        </div>

        <div style="margin-top:24px;">
            <button @click="spin"
                    :disabled="spinning || !canSpin || !!result"
                    style="padding:12px 36px;font-size:16px;background:#7c3aed;color:#fff;border:none;border-radius:8px;cursor:pointer;opacity:1;"
                    :style="{ opacity: (spinning || !canSpin || !!result) ? 0.5 : 1, cursor: (spinning || !canSpin || !!result) ? 'not-allowed' : 'pointer' }">
                {{ spinning ? 'Spinning...' : '🎰 SPIN NOW' }}
            </button>
        </div>
    </div>`
});
```

- [ ] **Step 2: Verify the JS file is syntactically valid**

Open `spinwheel.js` in your editor and ensure no obvious syntax errors. Run the app and navigate to `/spin` to do a visual check (see Task 11).

- [ ] **Step 3: Commit**

```
git add src/Plugins/Promotion.SpinWheel/wwwroot/
git commit -m "feat(spin-wheel): add Vue wheel component with SVG, hover tooltips, spin animation"
```

---

## Task 11: Plugin Wiring and End-to-End Smoke Test

- [ ] **Step 1: Add the plugin to the Grand.Web project references**

Open `src/Web/Grand.Web/Grand.Web.csproj`. Add a reference to the new plugin (follow the pattern of other plugins already listed):

```xml
<ProjectReference Include="..\..\Plugins\Promotion.SpinWheel\Promotion.SpinWheel.csproj">
    <Private>false</Private>
    <ExcludeAssets>runtime</ExcludeAssets>
</ProjectReference>
```

- [ ] **Step 2: Register plugin type in plugin manifest**

In `src/Web/Grand.Web/App_Data/plugins.json` (or wherever GrandNode's plugin registry lives — check other plugins for the exact file), add:

```json
{
    "SystemName": "Promotion.SpinWheel",
    "Group": "Promotion plugins",
    "FriendlyName": "Spin to Win",
    "SupportedVersion": "2.4",
    "DisplayOrder": 1,
    "FileName": "Promotion.SpinWheel.dll"
}
```

> Check `src/Web/Grand.Web/App_Data/plugins.json` for the exact JSON format used by other entries and match it.

- [ ] **Step 3: Run all tests — verify nothing is broken**

```
dotnet test src/Tests/Grand.Plugins.SpinWheel.Tests/
```
Expected: All tests pass.

- [ ] **Step 4: Build and run the full app**

```
dotnet run --project src/Web/Grand.Web/Grand.Web.csproj
```

- [ ] **Step 5: Install plugin in admin**

Navigate to `Admin → Configuration → Plugins`. Find "Spin to Win" and click Install.

- [ ] **Step 6: Configure the wheel**

Navigate to `Admin → Promotions → Spin Wheel → Configure`. Add 3–4 segments. Set cooldown to 1 hour for testing. Save.

- [ ] **Step 7: Verify the spin page**

Log in as a customer. Navigate to `/spin`. Verify:
- [ ] Wheel renders with correct segment colors and proportional arcs
- [ ] Hovering a segment shows a tooltip with its label
- [ ] Clicking SPIN NOW animates the wheel for ~3 seconds
- [ ] Result screen shows the coupon code and "Applied to your cart"
- [ ] SPIN NOW button is disabled after spinning, countdown shown
- [ ] Opening the cart shows the discount applied

- [ ] **Step 8: Verify cooldown**

Log out, log in again, go to `/spin`. Verify the SPIN NOW button is still disabled and shows the countdown.

- [ ] **Step 9: Commit**

```
git add src/Web/Grand.Web/Grand.Web.csproj
git commit -m "feat(spin-wheel): wire plugin into Grand.Web and verify end-to-end"
```
