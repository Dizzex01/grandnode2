using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Domain.Catalog;
using Promotion.SpinWheel.Domain;

namespace Promotion.SpinWheel.Services;

public class SpinWheelMenuCategoryService(
    ICategoryService categoryService,
    ISettingService settingService)
{
    private const string SpinWheelSeName = "spin";

    /// <summary>
    /// Creates or updates the synthetic nav Category for the Spin to Win link.
    /// Stores the resulting category Id back into settings.
    /// </summary>
    public async Task UpsertMenuCategory(SpinWheelSettings settings, string storeId)
    {
        var category = string.IsNullOrEmpty(settings.MenuCategoryId)
            ? null
            : await categoryService.GetCategoryById(settings.MenuCategoryId);

        if (category == null) {
            category = new Category {
                Name = "Spin to Win",
                SeName = SpinWheelSeName,
                Flag = "Go!",
                FlagStyle = "badge-warning",
                IncludeInMenu = true,
                Published = true,
                DisplayOrder = settings.MenuDisplayOrder
            };
            await categoryService.InsertCategory(category);

            settings.MenuCategoryId = category.Id;
            await settingService.SaveSetting(settings, storeId);
        } else {
            category.DisplayOrder = settings.MenuDisplayOrder;
            category.IncludeInMenu = true;
            category.Published = true;
            await categoryService.UpdateCategory(category);
        }
    }

    /// <summary>
    /// Removes the synthetic nav Category (called on plugin uninstall or when menu is hidden).
    /// </summary>
    public async Task DeleteMenuCategory(SpinWheelSettings settings, string storeId)
    {
        if (string.IsNullOrEmpty(settings.MenuCategoryId))
            return;

        var category = await categoryService.GetCategoryById(settings.MenuCategoryId);
        if (category != null)
            await categoryService.DeleteCategory(category);

        settings.MenuCategoryId = string.Empty;
        await settingService.SaveSetting(settings, storeId);
    }
}
