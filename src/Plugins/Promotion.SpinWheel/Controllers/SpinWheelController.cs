using Grand.Infrastructure;
using Grand.Web.Common.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Promotion.SpinWheel.Commands;
using Promotion.SpinWheel.Queries;

namespace Promotion.SpinWheel.Controllers;

[Authorize]
public class SpinWheelController(
    IMediator mediator,
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

        return View(state);
    }

    [HttpPost]
    public async Task<IActionResult> Execute()
    {
        var customerId = contextAccessor.WorkContext.CurrentCustomer.Id;
        var storeId = contextAccessor.StoreContext.CurrentStore.Id;

        var result = await mediator.Send(new SpinWheelCommand {
            CustomerId = customerId,
            StoreId = storeId,
            CurrencyCode = contextAccessor.WorkContext.WorkingCurrency.CurrencyCode
        });

        return Json(result);
    }
}
