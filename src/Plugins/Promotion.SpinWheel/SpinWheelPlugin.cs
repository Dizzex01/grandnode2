using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Data;
using Grand.Domain.Admin;
using Grand.Infrastructure.Plugins;

namespace Promotion.SpinWheel;

public class SpinWheelPlugin(
    IPluginTranslateResource pluginTranslateResource,
    IRepository<AdminSiteMap> siteMapRepository)
    : BasePlugin, IPlugin
{
    public override async Task Install()
    {
        await pluginTranslateResource.AddOrUpdatePluginTranslateResource(
            "Plugins.Promotion.SpinWheel.Title", "Spin to Win");
        await pluginTranslateResource.AddOrUpdatePluginTranslateResource(
            "Plugins.Promotion.SpinWheel.Menu", "Spin Wheel");

        // Add menu entry under Marketing
        var marketing = siteMapRepository.Table.FirstOrDefault(x => x.SystemName == "Marketing");
        if (marketing != null && marketing.ChildNodes.All(c => c.SystemName != "SpinWheel")) {
            marketing.ChildNodes.Add(new AdminSiteMap {
                SystemName = "SpinWheel",
                ResourceName = "Plugins.Promotion.SpinWheel.Menu",
                ControllerName = "SpinWheelConfig",
                ActionName = "Configure",
                DisplayOrder = 99,
                IconClass = "fa fa-dot-circle-o"
            });
            await siteMapRepository.UpdateAsync(marketing);
        }

        await base.Install();
    }

    public override async Task Uninstall()
    {
        await pluginTranslateResource.DeletePluginTranslationResource(
            "Plugins.Promotion.SpinWheel.Title");
        await pluginTranslateResource.DeletePluginTranslationResource(
            "Plugins.Promotion.SpinWheel.Menu");

        // Remove menu entry
        var marketing = siteMapRepository.Table.FirstOrDefault(x => x.SystemName == "Marketing");
        if (marketing != null) {
            var node = marketing.ChildNodes.FirstOrDefault(c => c.SystemName == "SpinWheel");
            if (node != null) {
                marketing.ChildNodes.Remove(node);
                await siteMapRepository.UpdateAsync(marketing);
            }
        }

        await base.Uninstall();
    }
}
