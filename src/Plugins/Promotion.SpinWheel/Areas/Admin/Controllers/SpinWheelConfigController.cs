using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Infrastructure;
using Grand.Web.Common.Controllers;
using Microsoft.AspNetCore.Mvc;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Models;
using Promotion.SpinWheel.Services;

namespace Promotion.SpinWheel.Areas.Admin.Controllers;

[Area("Admin")]
public class SpinWheelConfigController(
    ISettingService settingService,
    IContextAccessor contextAccessor,
    SpinWheelMenuCategoryService menuCategoryService)
    : BaseAdminPluginController
{
    [HttpGet]
    public async Task<IActionResult> Configure()
    {
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;
        var settings = await settingService.LoadSetting<SpinWheelSettings>(storeId);

        var model = new ConfigureModel {
            Enabled = settings.Enabled,
            CooldownHours = settings.CooldownHours,
            MenuDisplayOrder = settings.MenuDisplayOrder,
            Segments = settings.Segments.Select(s => new SegmentConfigModel {
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
        var settings = await settingService.LoadSetting<SpinWheelSettings>(storeId);

        settings.Enabled = model.Enabled;
        settings.CooldownHours = model.CooldownHours;
        settings.MenuDisplayOrder = model.MenuDisplayOrder;
        settings.Segments = model.Segments.Select(s => new SpinSegment {
            Id = string.IsNullOrEmpty(s.Id) ? Guid.NewGuid().ToString("N") : s.Id,
            Label = s.Label,
            DiscountPercent = s.DiscountPercent,
            ProbabilityWeight = s.ProbabilityWeight,
            Color = s.Color,
            CouponPrefix = s.CouponPrefix.ToUpperInvariant()
        }).ToList();

        await settingService.SaveSetting(settings, storeId);
        await menuCategoryService.UpsertMenuCategory(settings, storeId);

        TempData["success"] = "Spin Wheel configuration saved.";
        return RedirectToAction(nameof(Configure));
    }
}
