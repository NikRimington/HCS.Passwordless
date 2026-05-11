using HCS.Passwordless.Configuration;

namespace HCS.Passwordless.MagicLink.Models;

/// <summary>View model for the magic-link confirmation page.</summary>
public sealed class MagicLinkConfirmModel
{
    public string Email { get; init; } = "";
    public string Token { get; init; } = "";
    public string ReturnUrl { get; init; } = "";
    public string PostUrl { get; init; } = "/auth/magic-link/verify";
    public string AntiForgeryFieldName { get; init; } = "__RequestVerificationToken";
    public string AntiForgeryToken { get; init; } = "";
    public BrandingOptions Branding { get; init; } = new();
}
