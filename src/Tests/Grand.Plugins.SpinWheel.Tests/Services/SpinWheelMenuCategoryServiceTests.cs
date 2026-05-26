using Grand.Business.Core.Interfaces.Catalog.Categories;
using Grand.Business.Core.Interfaces.Common.Configuration;
using Grand.Domain.Catalog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Promotion.SpinWheel.Domain;
using Promotion.SpinWheel.Services;

namespace Grand.Plugins.SpinWheel.Tests.Services;

[TestClass]
public class SpinWheelMenuCategoryServiceTests
{
    private SpinWheelMenuCategoryService _service;
    private Mock<ICategoryService> _categoryServiceMock;
    private Mock<ISettingService> _settingServiceMock;

    [TestInitialize]
    public void Init()
    {
        _categoryServiceMock = new Mock<ICategoryService>();
        _settingServiceMock = new Mock<ISettingService>();
        _service = new SpinWheelMenuCategoryService(_categoryServiceMock.Object, _settingServiceMock.Object);
    }

    [TestMethod]
    public async Task UpsertMenuCategory_NoCategoryId_InsertsNewCategory()
    {
        var settings = new SpinWheelSettings { MenuDisplayOrder = 42 };
        Category inserted = null;

        _categoryServiceMock
            .Setup(c => c.InsertCategory(It.IsAny<Category>()))
            .Callback<Category>(c => { c.Id = "new-id"; inserted = c; })
            .Returns(Task.CompletedTask);
        _settingServiceMock
            .Setup(s => s.SaveSetting(It.IsAny<SpinWheelSettings>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _service.UpsertMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.InsertCategory(It.IsAny<Category>()), Times.Once);
        Assert.IsNotNull(inserted);
        Assert.AreEqual("Spin to Win", inserted.Name);
        Assert.AreEqual("spin", inserted.SeName);
        Assert.AreEqual("Go!", inserted.Flag);
        Assert.AreEqual("badge-warning", inserted.FlagStyle);
        Assert.IsTrue(inserted.IncludeInMenu);
        Assert.IsTrue(inserted.Published);
        Assert.AreEqual(42, inserted.DisplayOrder);
        Assert.AreEqual("new-id", settings.MenuCategoryId);
    }

    [TestMethod]
    public async Task UpsertMenuCategory_ExistingCategoryId_UpdatesDisplayOrder()
    {
        var existing = new Category { Id = "existing-id", DisplayOrder = 10 };
        var settings = new SpinWheelSettings { MenuCategoryId = "existing-id", MenuDisplayOrder = 55 };

        _categoryServiceMock
            .Setup(c => c.GetCategoryById("existing-id"))
            .ReturnsAsync(existing);
        _categoryServiceMock
            .Setup(c => c.UpdateCategory(It.IsAny<Category>()))
            .Returns(Task.CompletedTask);

        await _service.UpsertMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.InsertCategory(It.IsAny<Category>()), Times.Never);
        _categoryServiceMock.Verify(c => c.UpdateCategory(existing), Times.Once);
        Assert.AreEqual(55, existing.DisplayOrder);
        Assert.IsTrue(existing.IncludeInMenu);
        Assert.IsTrue(existing.Published);
    }

    [TestMethod]
    public async Task UpsertMenuCategory_StaleId_InsertsNewCategory()
    {
        // MenuCategoryId set but category no longer exists in DB
        var settings = new SpinWheelSettings { MenuCategoryId = "gone-id", MenuDisplayOrder = 1 };

        _categoryServiceMock
            .Setup(c => c.GetCategoryById("gone-id"))
            .ReturnsAsync((Category)null);
        _categoryServiceMock
            .Setup(c => c.InsertCategory(It.IsAny<Category>()))
            .Callback<Category>(c => c.Id = "new-id")
            .Returns(Task.CompletedTask);
        _settingServiceMock
            .Setup(s => s.SaveSetting(It.IsAny<SpinWheelSettings>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _service.UpsertMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.InsertCategory(It.IsAny<Category>()), Times.Once);
        Assert.AreEqual("new-id", settings.MenuCategoryId);
    }

    [TestMethod]
    public async Task DeleteMenuCategory_ExistingCategory_DeletesAndClearsId()
    {
        var existing = new Category { Id = "cat-id" };
        var settings = new SpinWheelSettings { MenuCategoryId = "cat-id" };

        _categoryServiceMock
            .Setup(c => c.GetCategoryById("cat-id"))
            .ReturnsAsync(existing);
        _categoryServiceMock
            .Setup(c => c.DeleteCategory(existing))
            .Returns(Task.CompletedTask);
        _settingServiceMock
            .Setup(s => s.SaveSetting(It.IsAny<SpinWheelSettings>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _service.DeleteMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.DeleteCategory(existing), Times.Once);
        Assert.AreEqual(string.Empty, settings.MenuCategoryId);
    }

    [TestMethod]
    public async Task DeleteMenuCategory_EmptyId_DoesNothing()
    {
        var settings = new SpinWheelSettings { MenuCategoryId = string.Empty };

        await _service.DeleteMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.GetCategoryById(It.IsAny<string>()), Times.Never);
        _categoryServiceMock.Verify(c => c.DeleteCategory(It.IsAny<Category>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteMenuCategory_CategoryNotFoundInDb_StillClearsId()
    {
        var settings = new SpinWheelSettings { MenuCategoryId = "missing-id" };

        _categoryServiceMock
            .Setup(c => c.GetCategoryById("missing-id"))
            .ReturnsAsync((Category)null);
        _settingServiceMock
            .Setup(s => s.SaveSetting(It.IsAny<SpinWheelSettings>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await _service.DeleteMenuCategory(settings, "store1");

        _categoryServiceMock.Verify(c => c.DeleteCategory(It.IsAny<Category>()), Times.Never);
        Assert.AreEqual(string.Empty, settings.MenuCategoryId);
    }
}
