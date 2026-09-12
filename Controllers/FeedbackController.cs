using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfitNx.CRM.Services;
using ProfitNx.CRM.ViewModels;

namespace ProfitNx.CRM.Controllers;

[AllowAnonymous]
public class FeedbackController : Controller
{
    private readonly IImplementationService _implementationService;

    public FeedbackController(IImplementationService implementationService) => _implementationService = implementationService;

    [HttpGet("Feedback/Training/{token}")]
    public async Task<IActionResult> Training(string token)
    {
        var item = await _implementationService.GetByFeedbackTokenAsync(token);
        if (item == null) return View("Invalid");
        if (item.FeedbackDate.HasValue)
        {
            ViewBag.AlreadySubmitted = true;
            ViewBag.Rating = item.FeedbackRating;
            return View("Thanks");
        }
        return View(new CustomerFeedbackViewModel { Token = token, FirmName = item.FirmName, ProductName = item.ProductName, Rating = 5 });
    }

    [HttpPost("Feedback/Training")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Training(CustomerFeedbackViewModel model)
    {
        var item = await _implementationService.GetByFeedbackTokenAsync(model.Token);
        if (item == null) return View("Invalid");
        if (!ModelState.IsValid)
        {
            model.FirmName = item.FirmName;
            model.ProductName = item.ProductName;
            return View(model);
        }
        await _implementationService.SubmitFeedbackAsync(model.Token, model.Rating, model.Remarks);
        ViewBag.Rating = model.Rating;
        return View("Thanks");
    }
}
