using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Infrastructure.Plugins;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Services;

namespace Promotion.SpinWheel;

public class SpinWheelPlugin(
    IPluginTranslateResource pluginTranslateResource,
    ISettingService settingService,
    ICategoryService categoryService)
    : BasePlugin, IPlugin
{
    public override string ConfigurationUrl() => "/Admin/SpinWheelConfig/Configure";

    public override async Task Install()
    {
        await pluginTranslateResource.AddOrUpdatePluginTranslateResource(
            "Plugins.Promotion.SpinWheel.Title", "Spin to Win");

        var menuService = new SpinWheelMenuCategoryService(categoryService, settingService);
        var settings = new SpinWheelSettings();
        await menuService.UpsertMenuCategory(settings, string.Empty);

        await base.Install();
    }

    public override async Task Uninstall()
    {
        await pluginTranslateResource.DeletePluginTranslationResource(
            "Plugins.Promotion.SpinWheel.Title");

        var menuService = new SpinWheelMenuCategoryService(categoryService, settingService);
        var settings = await settingService.LoadSetting<SpinWheelSettings>(string.Empty);
        await menuService.DeleteMenuCategory(settings, string.Empty);

        await base.Uninstall();
    }
}
