using System.Security.Cryptography;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using HCS.Passwordless.Configuration;
using HCS.Passwordless.Endpoints.Shared;
using HCS.Passwordless.RateLimiting;
using HCS.Passwordless.Services;
using HCS.Passwordless.WebAuthn.Configuration;
using HCS.Passwordless.WebAuthn.Dtos;
using HCS.Passwordless.Core.Filters;
using HCS.Passwordless.WebAuthn.Notifications;
using HCS.Passwordless.WebAuthn.Services;
using HCS.Passwordless.WebAuthn.Storage;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Security;
using Microsoft.Extensions.Logging;

namespace HCS.Passwordless.WebAuthn.Controllers;

[ApiController]
[Route("auth/webauthn")]
public partial class WebAuthnController : ControllerBase
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberLookupService _lookup;
    private readonly IMemberCredentialStore _store;
    private readonly IFido2 _fido2;
    private readonly IWebAuthnChallengeStore _challenges;
    private readonly IPasswordlessSignInService _signIn;
    private readonly IPasswordlessRateLimiter _limiter;
    private readonly IEventAggregator _events;
    private readonly IOptionsMonitor<PasswordlessOptions> _baseOpts;
    private readonly IOptionsMonitor<WebAuthnOptions> _waOpts;
    private readonly ILogger<WebAuthnController> _logger;
    private readonly IPasswordlessClock _clock;


    public WebAuthnController(
        IMemberManager memberManager,
        IMemberLookupService lookup,
        IMemberCredentialStore store,
        IFido2 fido2,
        IWebAuthnChallengeStore challenges,
        IPasswordlessSignInService signIn,
        IPasswordlessRateLimiter limiter,
        IEventAggregator events,
        IOptionsMonitor<PasswordlessOptions> baseOpts,
        IOptionsMonitor<WebAuthnOptions> waOpts,
        ILogger<WebAuthnController> logger,
        IPasswordlessClock clock)
    {
        _memberManager = memberManager;
        _lookup = lookup;
        _store = store;
        _fido2 = fido2;
        _challenges = challenges;
        _signIn = signIn;
        _limiter = limiter;
        _events = events;
        _baseOpts = baseOpts;
        _waOpts = waOpts;
        _logger = logger;
        _clock = clock;
    }

    [HttpPost("register/options")]
    [ValidateAntiForgeryToken]
    [Authorize]
    [EnumAsIntegerJson]
    public async Task<IActionResult> RegisterOptions([FromBody] RegisterOptionsRequest dto, CancellationToken ct)
    {
        var waOpts = _waOpts.CurrentValue;
        if (!waOpts.Enabled) return NotFound();

        var member = await _memberManager.GetCurrentMemberAsync();
        if (member is null) return Forbid();

        var existing = await _store.GetByMemberAsync(member.Key, ct);
        var excludeList = existing.Select(c => new PublicKeyCredentialDescriptor(c.CredentialId)).ToList();
        LogRegisterOptionsStarted(member.Key, excludeList.Count);

        var fido2User = new Fido2User
        {
            Id = member.Key.ToByteArray(),
            Name = member.Email ?? member.UserName ?? member.Id,
            DisplayName = member.Name ?? member.Email ?? member.UserName ?? member.Id
        };

        var authenticatorSelection = new AuthenticatorSelection
        {
            ResidentKey = waOpts.ResidentKey,
            UserVerification = waOpts.UserVerification,
            AuthenticatorAttachment = waOpts.AuthenticatorAttachment
        };

        var createOptions = _fido2.RequestNewCredential(
            new RequestNewCredentialParams
            {
                User = fido2User,
                ExcludeCredentials = excludeList,
                AuthenticatorSelection = authenticatorSelection,
                AttestationPreference = waOpts.AttestationPreference
            });

        var ceremonyId = $"pwl:webauthn:reg:{member.Key}:{Guid.NewGuid()}";
        var state = new RegistrationCeremonyState(createOptions, dto.Nickname, member.Key);
        await _challenges.PutAsync(ceremonyId, state, waOpts.ChallengeTtl, ct);
        LogRegisterOptionsCeremonyCreated(member.Key);

        return Ok(new { CeremonyId = ceremonyId, Options = createOptions });
    }

    [HttpPost("register/complete")]
    [ValidateAntiForgeryToken]
    [Authorize]
    [EnumAsIntegerJson]
    public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteRequest dto, CancellationToken ct)
    {
        if (!_waOpts.CurrentValue.Enabled) return NotFound();

        var member = await _memberManager.GetCurrentMemberAsync();
        if (member is null) return Forbid();

        var state = await _challenges.TakeAsync<RegistrationCeremonyState>(dto.CeremonyId, ct);
        if (state is null || state.MemberKey != member.Key)
        {
            LogRegisterCompleteInvalidCeremony(dto.CeremonyId);
            return BadRequest(new { error = "invalid_ceremony" });
        }

        IsCredentialIdUniqueToUserAsyncDelegate isUnique = async (args, innerCt) =>
        {
            var found = await _store.GetByCredentialIdAsync(args.CredentialId, innerCt);
            return found is null;
        };

        var attestation = new AuthenticatorAttestationRawResponse
        {
            Id = dto.Attestation.Id,
            RawId = WebEncoders.Base64UrlDecode(dto.Attestation.RawId),
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAttestationRawResponse.AttestationResponse
            {
                AttestationObject = WebEncoders.Base64UrlDecode(dto.Attestation.Response.AttestationObject),
                ClientDataJson = WebEncoders.Base64UrlDecode(dto.Attestation.Response.ClientDataJSON),
                Transports = dto.Attestation.Response.Transports?
                    .Select(t => Enum.TryParse<AuthenticatorTransport>(t, ignoreCase: true, out var v) ? (AuthenticatorTransport?)v : null)
                    .OfType<AuthenticatorTransport>()
                    .ToArray()
            }
        };

        RegisteredPublicKeyCredential result;
        try
        {
            result = await _fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = attestation,
                    OriginalOptions = state.Options,
                    IsCredentialIdUniqueToUserCallback = isUnique
                }, ct);
        }
        catch (Exception ex)
        {
            var refGuid = Guid.NewGuid();
            LogError(ex, refGuid);
            return BadRequest(new { error = "attestation_failed", detail = $"Reference: {refGuid}" });
        }

        var credential = new StoredCredential(
            Id: Guid.NewGuid(),
            MemberKey: member.Key,
            CredentialId: result.Id,
            PublicKey: result.PublicKey,
            UserHandle: member.Key.ToByteArray(),
            SignatureCounter: result.SignCount,
            CredType: "public-key",
            AaGuid: result.AaGuid,
            Transports: result.Transports is not null ? string.Join(",", result.Transports) : null,
            BackupEligible: result.IsBackupEligible,
            BackupState: result.IsBackedUp,
            Nickname: state.Nickname,
            CreatedUtc: _clock.UtcNow.UtcDateTime,
            LastUsedUtc: null,
            AttestationFormat: result.AttestationFormat);

        var saved = await _store.AddAsync(credential, ct);
        LogRegisterCredentialSaved(member.Key, saved.AaGuid, saved.AttestationFormat);

        return Ok(new CredentialView(
            saved.Id, saved.Nickname, null, saved.AaGuid,
            saved.CreatedUtc, saved.LastUsedUtc,
            saved.BackupEligible, saved.BackupState, saved.Transports));
    }

    [HttpPost("signin/options")]
    [EnumAsIntegerJson]
    public async Task<IActionResult> SignInOptions([FromBody] SignInOptionsRequest dto, CancellationToken ct)
    {
        var waOpts = _waOpts.CurrentValue;
        if (!waOpts.Enabled) return NotFound();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await _limiter.TryAcquireAsync($"webauthn-signin-options:ip:{ip}", TimeSpan.FromMinutes(1), waOpts.SignInOptionsPerIpPerMinute, ct))
        {
            LogSignInRateLimited(ip);
            return StatusCode(429);
        }

        List<PublicKeyCredentialDescriptor> allowList;
        Guid? memberKey = null;
        bool isDecoy;
        bool memberFound = false;
        int credentialCount = 0;

        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            allowList = [];
            isDecoy = false;
        }
        else
        {
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
            var member = await _lookup.FindApprovedAsync(normalizedEmail, ct);
            if (member is not null)
            {
                memberFound = true;
                var credentials = await _store.GetByMemberAsync(member.Key, ct);
                credentialCount = credentials.Count;
                if (credentials.Count > 0)
                {
                    allowList = [.. credentials.Select(c => new PublicKeyCredentialDescriptor(c.CredentialId))];
                    memberKey = member.Key;
                    isDecoy = false;
                }
                else
                {
                    allowList = BuildDecoyAllowList(normalizedEmail);
                    isDecoy = true;
                }
            }
            else
            {
                allowList = BuildDecoyAllowList(normalizedEmail);
                isDecoy = true;
            }
        }

        LogSignInOptionsCreated(!string.IsNullOrWhiteSpace(dto.Email), memberFound, credentialCount, isDecoy);

        var assertionOptions = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowList,
            UserVerification = waOpts.UserVerification
        });

        var ceremonyId = $"pwl:webauthn:sig:{Guid.NewGuid()}";
        var state = new AssertionCeremonyState(assertionOptions, memberKey, isDecoy);
        await _challenges.PutAsync(ceremonyId, state, waOpts.ChallengeTtl, ct);

        return Ok(new { CeremonyId = ceremonyId, Options = assertionOptions });
    }

    [HttpPost("signin/complete")]
    [EnumAsIntegerJson]
    public async Task<IActionResult> SignInComplete([FromBody] SignInCompleteRequest dto, CancellationToken ct)
    {
        if (!_waOpts.CurrentValue.Enabled) return NotFound();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!await _limiter.TryAcquireAsync($"webauthn-signin-complete:ip:{ip}", TimeSpan.FromMinutes(1), _waOpts.CurrentValue.SignInCompletePerIpPerMinute, ct))
        {
            LogSignInRateLimited(ip);
            return StatusCode(429);
        }

        var state = await _challenges.TakeAsync<AssertionCeremonyState>(dto.CeremonyId, ct);
        if (state is null)
        {
            LogSignInCeremonyNotFound(dto.CeremonyId);
            return BadRequest(new { error = "invalid_ceremony" });
        }

        if (state.IsDecoy)
        {
            LogSignInDecoy();
            await FakeWork.DelayAsync(_baseOpts.CurrentValue.RateLimits.FakeWorkDelay, ct);
            return Unauthorized();
        }

        var rawId = WebEncoders.Base64UrlDecode(dto.Assertion.RawId);
        var storedCredential = await _store.GetByCredentialIdAsync(rawId, ct);
        if (storedCredential is null)
        {
            LogSignInCredentialNotFound();
            return Unauthorized();
        }

        var userHandle = dto.Assertion.Response.UserHandle is not null
            ? WebEncoders.Base64UrlDecode(dto.Assertion.Response.UserHandle)
            : null;

        var memberLookupHandle = state.MemberKey.HasValue
            ? state.MemberKey.Value.ToByteArray()
            : userHandle ?? Array.Empty<byte>();
        var member = await _lookup.FindApprovedByUserHandleAsync(memberLookupHandle, ct);

        if (member is null)
        {
            LogSignInMemberNotFound(state.MemberKey, userHandle is not null);
            return Unauthorized();
        }
        if (state.MemberKey.HasValue && member.Key != state.MemberKey.Value)
        {
            LogSignInMemberKeyMismatch(state.MemberKey.Value, member.Key);
            return Unauthorized();
        }
        if (storedCredential.MemberKey != member.Key)
        {
            LogSignInCredentialOwnerMismatch(storedCredential.MemberKey, member.Key);
            return Unauthorized();
        }

        var assertion = new AuthenticatorAssertionRawResponse
        {
            Id = dto.Assertion.Id,
            RawId = rawId,
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAssertionRawResponse.AssertionResponse
            {
                AuthenticatorData = WebEncoders.Base64UrlDecode(dto.Assertion.Response.AuthenticatorData),
                ClientDataJson = WebEncoders.Base64UrlDecode(dto.Assertion.Response.ClientDataJson),
                Signature = WebEncoders.Base64UrlDecode(dto.Assertion.Response.Signature),
                UserHandle = userHandle
            }
        };

        VerifyAssertionResult result;
        try
        {
            result = await _fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = assertion,
                    OriginalOptions = state.Options,
                    StoredPublicKey = storedCredential.PublicKey,
                    StoredSignatureCounter = storedCredential.SignatureCounter,
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                        Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(storedCredential.UserHandle))
                }, ct);
        }
        catch (Exception ex)
        {
            var refGuid = Guid.NewGuid();
            LogError(ex, refGuid);
            return BadRequest(new { error = "assertion_failed", detail = $"Reference: {refGuid}" });
        }

        var counterRegressed =
            (result.SignCount != 0 && result.SignCount <= storedCredential.SignatureCounter) ||
            (result.SignCount == 0 && storedCredential.HasEverIncrementedCounter);

        if (counterRegressed)
        {
            LogSignInCounterRegression(storedCredential.SignatureCounter, result.SignCount, storedCredential.MemberKey);
            await _events.PublishAsync(new PasskeyCounterRegressionNotification
            {
                MemberKey = storedCredential.MemberKey,
                CredentialId = storedCredential.CredentialId,
                StoredCounter = storedCredential.SignatureCounter,
                ReceivedCounter = result.SignCount
            });
            return Unauthorized();
        }

        var hasEverIncremented = storedCredential.HasEverIncrementedCounter || result.SignCount > 0;
        await _store.UpdateAfterAssertionAsync(storedCredential.CredentialId, result.SignCount, _clock.UtcNow.UtcDateTime, hasEverIncremented, ct);
        await _signIn.SignInAndRotateAsync(member, isPersistent: true, authenticationMethod: "webauthn", ct: ct);
        LogSignInSuccess(member.Key);

        return Ok(new { ok = true });
    }

    private List<PublicKeyCredentialDescriptor> BuildDecoyAllowList(string email)
    {
        var seed = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_waOpts.CurrentValue.DecoyHmacKey!),
            Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return
        [
            new(seed[..32])
        ];
    }
}
