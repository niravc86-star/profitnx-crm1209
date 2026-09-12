using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProfitNx.CRM.Models;
using ProfitNx.CRM.Services;

namespace ProfitNx.CRM.Controllers;

[Authorize]
[IgnoreAntiforgeryToken]
public class PushController : Controller
{
    private readonly IWebPushService _webPush;

    public PushController(IWebPushService webPush)
    {
        _webPush = webPush;
    }

    [HttpGet]
    public IActionResult PublicKey()
    {
        if (!_webPush.IsConfigured)
            return Json(new { configured = false, publicKey = "" });
        return Json(new { configured = true, publicKey = _webPush.GetPublicKey() });
    }

    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Endpoint) ||
            request.Keys == null ||
            string.IsNullOrWhiteSpace(request.Keys.P256dh) ||
            string.IsNullOrWhiteSpace(request.Keys.Auth))
        {
            return BadRequest(new { ok = false, error = "Invalid subscription" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userName = User.Identity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var ua = Request.Headers.UserAgent.ToString();
        await _webPush.SaveSubscriptionAsync(userId, userName, request.Endpoint.Trim(),
            request.Keys.P256dh.Trim(), request.Keys.Auth.Trim(), ua);
        return Json(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> Unsubscribe([FromBody] PushSubscribeRequest? request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var endpoint = request?.Endpoint?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
            return BadRequest(new { ok = false });

        await _webPush.RemoveSubscriptionAsync(userId, endpoint);
        return Json(new { ok = true });
    }
}
