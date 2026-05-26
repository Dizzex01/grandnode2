# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Restore and build the full solution
dotnet restore ./GrandNode.sln
dotnet build ./GrandNode.sln --configuration Release

# Install .NET Aspire workload (required once)
dotnet workload install aspire

# Run the web app
dotnet run --project src/Web/Grand.Web

# Run all tests (requires MongoDB on port 27017)
docker run -d -p 27017:27017 mongo
dotnet test ./src/Tests/

# Run a single test project
dotnet test ./src/Tests/Grand.Business.Catalog.Tests/Grand.Business.Catalog.Tests.csproj

# Publish for deployment
dotnet publish src/Web/Grand.Web -c Release -o /var/webapps/grandnode
```

Code style is enforced by `.editorconfig` (Allman braces, `var` preference). All NuGet package versions are centralized in `Directory.Packages.props` — add packages there, not in individual `.csproj` files.

## Architecture

GrandNode2 is an ASP.NET Core 10.0 e-commerce platform using **MongoDB** (not relational) and **MediatR** for CQRS. The solution has ~80 projects organized into these layers:

### Layers (bottom to top)

| Layer | Path | Purpose |
|---|---|---|
| Core | `src/Core/` | Data access (MongoDB), domain models, DI infrastructure, AutoMapper |
| Business | `src/Business/` | Domain logic split by bounded context (Catalog, Checkout, Marketing, etc.) |
| Web | `src/Web/` | ASP.NET Core app (`Grand.Web`), Admin area (`Grand.Web.Admin`), Store UI (`Grand.Web.Store`), Vendor portal |
| Modules | `src/Modules/` | System modules: API, Installer, Migration, ScheduledTasks |
| Plugins | `src/Plugins/` | Optional feature plugins loaded at runtime |
| Tests | `src/Tests/` | One test project per Business/Core/Web project, plus per-plugin tests |

### Request flow

Controllers → MediatR `IRequest`/`IRequestHandler` (Commands mutate, Queries read) → Business services → `IRepository<T>` (MongoDB) → MongoDB. FluentValidation validators are registered alongside MediatR handlers.

### Business layer conventions

Each project under `src/Business/` is a bounded context (Catalog, Checkout, Customers, Marketing, Messages, Cms, Common, Authentication, Storage). Within each:

- `Commands/` — MediatR command handlers (writes/mutations)
- `Queries/` — MediatR query handlers (reads)
- `Events/` — Domain event handlers
- `Services/` — Core domain service implementations
- `Infrastructure/StartupApplication.cs` — DI registration via `IStartupApplication`

### Plugin system

Plugins are Razor class library projects (`Microsoft.NET.Sdk.Razor`) that output into `src/Web/Grand.Web/Plugins/<PluginName>/`. They are loaded dynamically at runtime. Key conventions:

- Reference core projects with `<Private>false</Private>` to avoid duplicating shared assemblies.
- Implement `IPlugin` (via `BasePlugin`) in `<PluginName>Plugin.cs` — `Install`/`Uninstall` hooks wire up admin menu entries and seed settings.
- Register services in `DependencyInjection.cs` (or `Infrastructure/StartupApplication.cs`) via `IStartupApplication`.
- Admin controllers live under `Areas/Admin/Controllers/` and must use the `Admin` area.
- `Manifest.cs` declares plugin metadata (system name, friendly name, group, version).
- `EndpointProvider.cs` maps routes for both store-facing and admin endpoints.
- `IPlugin` types are auto-registered via `AddScoped(t)` by `PluginManager` before `IStartupApplication.ConfigureServices` runs — so plugin constructor dependencies must be registered by the core app, not by the plugin's own startup.

### Key cross-cutting patterns

- **Settings**: Plugin/module settings are stored as MongoDB documents via `ISettingService.SaveSetting<T>()`. Retrieve with `ISettingService.LoadSetting<T>()`.
- **Localization**: String resources are added in `Install()` via `IPluginTranslateResource.AddOrUpdatePluginTranslateResource()` and cleaned up in `Uninstall()`.
- **Discount currency**: Always set `CurrencyCode` on `Discount` entities — `DiscountValidationService` rejects discounts whose currency doesn't match the working currency.
- **Coupon flow**: Coupons are created in the `DiscountCoupon` collection (`Used=false`). Apply to a customer's cart by writing to `Customer.UserFields["DiscountCoupons"]` via `customerService.UpdateUserField`. The `DiscountCouponValidator` only runs on manual form submission — direct `UpdateUserField` bypasses it safely.
- **MongoDB schema changes**: No EF migrations — use `src/Modules/Grand.Module.Migration/` migration scripts for schema changes.
- **Nav menu ordering**: Categories are sorted by `DisplayOrder` (ascending) in `GetMenuHandler`. Set a category's `DisplayOrder` to position it between existing items.