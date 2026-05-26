using Grand.Infrastructure.Endpoints;
using Microsoft.AspNetCore.Builder;
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
