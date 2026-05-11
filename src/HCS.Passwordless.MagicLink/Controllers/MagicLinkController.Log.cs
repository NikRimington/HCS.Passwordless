using Microsoft.Extensions.Logging;

namespace HCS.Passwordless.MagicLink.Controllers;

public partial class MagicLinkController
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: showing confirm page email={Email} returnUrl={ReturnUrl}")]
    private partial void LogVerifyShowingConfirmPage(string? email, string? returnUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: email={Email} returnUrl={ReturnUrl}")]
    private partial void LogVerifyAttempt(string? email, string? returnUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: missing email or token — aborting")]
    private partial void LogVerifyAborting();

    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: rate-limited ip={Ip}")]
    private partial void LogRateLimited(string ip);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: member not found or not approved for email={Email}")]
    private partial void LogVerifyMemberNotFound(string? email);

    [LoggerMessage(Level = LogLevel.Debug, Message = "MagicLink verify: member found id={MemberId} stamp={Stamp}")]
    private partial void LogVerifyMemberFound(string memberId, string? stamp);

    [LoggerMessage(Level = LogLevel.Information, Message = "MagicLink verify: single-use check isFirstUse={IsFirstUse}")]
    private partial void LogVerifySingleUseCheck(bool isFirstUse);

    [LoggerMessage(Level = LogLevel.Information, Message = "MagicLink verify: VerifyUserTokenAsync result={Valid} provider={Provider} purpose={Purpose}")]
    private partial void LogVerifyUserTokenValidation(bool valid, string provider, string purpose);

    [LoggerMessage(Level = LogLevel.Information, Message = "MagicLink verify: token valid — calling SignInAndRotateAsync memberId={MemberId}")]
    private partial void LogVerifyTokenValid(string memberId);

    [LoggerMessage(Level = LogLevel.Information, Message = "MagicLink verify: sign-in complete — redirecting to {Safe}")]
    private partial void LogVerifySignedInAndRedirecting(string safe);
}