using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HCS.Passwordless.Configuration;
using HCS.Passwordless.MagicLink.Configuration;
using HCS.Passwordless.MagicLink.Models;
using HCS.Passwordless.Endpoints.Dtos;
using HCS.Passwordless.Endpoints.Shared;
using HCS.Passwordless.Notifications;
using HCS.Passwordless.MagicLink.Notifications;
using HCS.Passwordless.RateLimiting;
using HCS.Passwordless.Security.TokenProviders;
using HCS.Passwordless.Services;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Controllers;

namespace HCS.Passwordless.MagicLink.Controllers;

[ApiController]
[Route("auth/magic-link")]
public partial class MagicLinkController : UmbracoApiController
{
    private readonly IMemberLookupService _lookup;
    private readonly UserManager<MemberIdentityUser> _users;
    private readonly IPasswordlessNotificationSender _sender;
    private readonly IPasswordlessSignInService _signIn;
    private readonly ISingleUseTokenStore _singleUse;
    private readonly IPasswordlessRateLimiter _limiter;
    private readonly IOptionsMonitor<PasswordlessOptions> _opts;
    private readonly IOptionsMonitor<MagicLinkOptions> _mlOpts;
    private readonly ILogger<MagicLinkController> _logger;
    private readonly LinkGenerator _linkGenerator;
    private readonly IAntiforgery _antiforgery;
    private readonly IRazorViewRenderer _viewRenderer;

    public MagicLinkController(
        IMemberLookupService lookup,
        UserManager<MemberIdentityUser> users,
        IPasswordlessNotificationSender sender,
        IPasswordlessSignInService signIn,
        ISingleUseTokenStore singleUse,
        IPasswordlessRateLimiter limiter,
        IOptionsMonitor<PasswordlessOptions> opts,
        IOptionsMonitor<MagicLinkOptions> mlOpts,
        ILogger<MagicLinkController> logger,
        LinkGenerator linkGenerator,
        IAntiforgery antiforgery,
        IRazorViewRenderer viewRenderer)
    {
        _lookup = lookup;
        _users = users;
        _sender = sender;
        _signIn = signIn;
        _singleUse = singleUse;
        _limiter = limiter;
        _opts = opts;
        _mlOpts = mlOpts;
        _logger = logger;
        _linkGenerator = linkGenerator;
        _antiforgery = antiforgery;
        _viewRenderer = viewRenderer;
    }

    [HttpPost("request")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendLink([FromBody] RequestByEmailDto dto, CancellationToken ct)
    {
        var mlOptions = _mlOpts.CurrentValue;
        if (!mlOptions.Enabled) return NotFound();

        var options = _opts.CurrentValue;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var normalizedEmail = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        var emailHash = Sha256Helper.Hash(normalizedEmail);

        if (!await _limiter.TryAcquireAsync($"ml-req:ip:{ip}", TimeSpan.FromMinutes(1), options.RateLimits.PerIpRequestsPerMinute, ct)
            || !await _limiter.TryAcquireAsync($"ml-req:email:{emailHash}", TimeSpan.FromHours(1), options.RateLimits.PerEmailRequestsPerHour, ct))
            return StatusCode(429);

        var member = await _lookup.FindApprovedAsync(normalizedEmail, ct);
        if (member is not null)
        {
            var token = await _users.GenerateUserTokenAsync(
                member, TokenProviderNames.MagicLink, TokenProviderNames.PurposeMagicLinkLogin);
            var link = BuildVerifyLink(member.Email!, token, dto.ReturnUrl, options);
            await _sender.SendMagicLinkAsync(member, link, mlOptions.TokenLifespan, ct);
        }
        else
        {
            await FakeWork.DelayAsync(options.RateLimits.FakeWorkDelay, ct);
        }

        return Accepted(new { ok = true });
    }

    /// <summary>
    /// Renders the confirmation page. Email scanners make GET requests to every link, so we must
    /// not consume the single-use token here — that happens only on the subsequent POST.
    /// </summary>
    [HttpGet("verify")]
    public async Task<IActionResult> Verify(
        [FromQuery] string? email,
        [FromQuery] string? token,
        [FromQuery] string? returnUrl,
        CancellationToken ct)
    {
        var mlOptions = _mlOpts.CurrentValue;
        if (!mlOptions.Enabled) return NotFound();

        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        var options = _opts.CurrentValue;
        var safe = ReturnUrlValidator.Sanitize(returnUrl, options.PostLoginRedirectPath);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            LogVerifyAborting();
            return Redirect($"{options.LoginPath}?error=expired-or-used&returnUrl={Uri.EscapeDataString(safe)}");
        }

        var afTokens = _antiforgery.GetAndStoreTokens(HttpContext);
        var postUrl = HttpContext.Request.Path.Value ?? "/auth/magic-link/verify";

        var model = new MagicLinkConfirmModel
        {
            Email = email.Trim(),
            Token = token,
            ReturnUrl = safe,
            PostUrl = postUrl,
            AntiForgeryFieldName = afTokens.FormFieldName,
            AntiForgeryToken = afTokens.RequestToken!,
            Branding = options.Notifications.Branding
        };

        LogVerifyShowingConfirmPage(email, safe);
        var html = await _viewRenderer.RenderAsync(
            "~/Views/Shared/Passwordless/MagicLinkConfirm.cshtml", model, ct);
        return Content(html, "text/html");
    }

    [HttpPost("verify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyConfirm(
        [FromForm] string? email,
        [FromForm] string? token,
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        var mlOptions = _mlOpts.CurrentValue;
        if (!mlOptions.Enabled) return NotFound();

        var options = _opts.CurrentValue;
        var safe = ReturnUrlValidator.Sanitize(returnUrl, options.PostLoginRedirectPath);
        var loginPath = $"{options.LoginPath}?error=expired-or-used&returnUrl={Uri.EscapeDataString(safe)}";

        LogVerifyAttempt(email, safe);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            LogVerifyAborting();
            return Redirect(loginPath);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await _limiter.TryAcquireAsync($"ml-verify:ip:{ip}", TimeSpan.FromMinutes(1), options.RateLimits.VerifyPerIpPerMinute, ct))
        {
            LogRateLimited(ip);
            return StatusCode(429);
        }

        var member = await _lookup.FindApprovedAsync(normalizedEmail, ct);
        if (member is null)
        {
            LogVerifyMemberNotFound(email);
            return Redirect(loginPath);
        }

        LogVerifyMemberFound(member.Id, member.SecurityStamp);

        var tokenHash = Sha256Helper.Hash(token);
        if (mlOptions.SingleUse)
        {
            var isFirstUse = await _singleUse.TryMarkUsedAsync(tokenHash, mlOptions.TokenLifespan, ct);
            LogVerifySingleUseCheck(isFirstUse);
            if (!isFirstUse) return Redirect(loginPath);
        }

        var valid = await _users.VerifyUserTokenAsync(
            member, TokenProviderNames.MagicLink, TokenProviderNames.PurposeMagicLinkLogin, token);

        LogVerifyUserTokenValidation(valid, TokenProviderNames.MagicLink, TokenProviderNames.PurposeMagicLinkLogin);

        if (!valid) return Redirect(loginPath);
        LogVerifyTokenValid(member.Id);
        await _signIn.SignInAndRotateAsync(member, isPersistent: true, authenticationMethod: "magic-link", ct: ct);
        LogVerifySignedInAndRedirecting(safe);

        return Redirect(safe);
    }

    private Uri BuildVerifyLink(string email, string token, string? returnUrl, PasswordlessOptions options)
    {
        var safe = ReturnUrlValidator.Sanitize(returnUrl, options.PostLoginRedirectPath);
        var uri = _linkGenerator.GetUriByAction(
            HttpContext,
            action: nameof(Verify),
            controller: "MagicLink",
            values: new { email, token, returnUrl = safe });
        return new Uri(uri!);
    }
}
