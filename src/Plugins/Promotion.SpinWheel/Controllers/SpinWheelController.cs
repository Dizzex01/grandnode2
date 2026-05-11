using Grand.Infrastructure;
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

        var state = await mediator.Send(new GetSpinStateQuery {
            CustomerId = customerId,
            StoreId = storeId
        });

        if (!state.IsEnabled)
            return RedirectToRoute("HomePage");

        await reconciliationService.EnsureApplied(customerId);

        return View(state);
    }

    [HttpPost]
    public async Task<IActionResult> Execute()
    {
        var customerId = contextAccessor.WorkContext.CurrentCustomer.Id;
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;

        var result = await mediator.Send(new SpinWheelCommand {
            CustomerId = customerId,
            StoreId = storeId
        });

        return Json(result);
    }
}
