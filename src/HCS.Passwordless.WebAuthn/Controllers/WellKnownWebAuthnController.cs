using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HCS.Passwordless.WebAuthn.Configuration;

namespace HCS.Passwordless.WebAuthn.Controllers;

/// <summary>
/// Serves the WebAuthn Related Origins document at <c>/.well-known/webauthn</c>.
/// Browsers fetch this resource from the Relying Party's domain to verify that an
/// alternative origin (e.g. <c>https://login.example.com</c>) is permitted to use the
/// configured <see cref="WebAuthnOptions.RpId"/> as its RP ID.
/// See: https://www.w3.org/TR/webauthn-3/#sctn-related-origins
/// </summary>
/// <remarks>
/// This endpoint intentionally returns <see cref="NotFoundResult"/> when WebAuthn is disabled
/// or when no valid related origins are configured, so callers do not receive a partial or
/// misleading configuration document.
/// </remarks>
[ApiController]
[Route("/.well-known/webauthn")]
[AllowAnonymous]
public sealed class WellKnownWebAuthnController : ControllerBase
{

    private readonly IOptionsMonitor<WebAuthnOptions> _waOpts;


    public WellKnownWebAuthnController(IOptionsMonitor<WebAuthnOptions> waOpts)
    {
        _waOpts = waOpts;
    }

    /// <summary>
    /// Returns the WebAuthn Related Origins JSON document for the current RP configuration.
    /// </summary>
    /// <returns>
    /// <para>
    /// <see cref="OkObjectResult"/> with payload <c>{ origins: string[] }</c> when WebAuthn is enabled
    /// and at least one configured origin is valid.
    /// </para>
    /// <para>
    /// <see cref="NotFoundResult"/> when WebAuthn is disabled, no origins are configured,
    /// or all configured origins fail validation.
    /// </para>
    /// </returns>
    /// <remarks>
    /// Validation rules:
    /// <list type="bullet">
    /// <item><description>Origin must be an absolute URI.</description></item>
    /// <item><description>Origin must use HTTPS.</description></item>
    /// <item><description>Origin must include a non-empty host.</description></item>
    /// </list>
    /// Valid origins are normalized by trimming whitespace, converting to lowercase,
    /// and removing duplicates before being returned.
    /// </remarks>
    [HttpGet]
    public IActionResult GetRelatedOrigins()
    {
        var opts = _waOpts.CurrentValue;

        if (!opts.Enabled)
            return NotFound();

        if (opts.Origins == null || !opts.Origins.Any())
            return NotFound();

        var sanitizedOrigins = opts.Origins?
            .Where(origin =>
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host);
                }
                return false;
            })
            .Select(origin => origin.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (sanitizedOrigins == null || sanitizedOrigins.Length == 0)
            return NotFound();

        return Ok(new { origins = sanitizedOrigins });
    }
}
