using Grand.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Promotion.SpinWheel.Services;

namespace Promotion.SpinWheel;

public class StartupApplication : IStartupApplication
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SpinWheelMenuCategoryService>();
    }

    public void Configure(WebApplication application, IWebHostEnvironment webHostEnvironment) { }

    public int Priority => 100;
    public bool BeforeConfigure => false;
}
