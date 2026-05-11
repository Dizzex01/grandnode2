using Grand.Business.Core.Interfaces.Common.Localization;
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
