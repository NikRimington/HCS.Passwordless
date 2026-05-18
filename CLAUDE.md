# HCS Passwordless — Solution CLAUDE.md

## What this is

A HCS-branded NuGet library suite for passwordless Umbraco 13 member authentication. Four packages: a shared core (infrastructure only), magic link, OTP add-on, and WebAuthn add-on. All delivered via NuGet; the demo site is for manual verification only.

## Solution layout

```
src/HCS.Passwordless.Core     # shared infrastructure — token store, rate limiter, sign-in
src/HCS.Passwordless.MagicLink # magic link auth (depends on Core)
src/HCS.Passwordless.Otp      # add-on — email OTP (depends on Core)
src/HCS.Passwordless.WebAuthn # add-on — FIDO2 passkeys (depends on Core)
tests/HCS.Passwordless.Tests  # xUnit suite (~121 tests)
demo/HCS.Passwordless.Demo    # runnable Umbraco 13 site
```

## Build & test

```bash
dotnet build
dotnet test                          # all tests
dotnet test --filter "Category=Otp"  # subset
```

## Configuration namespace

**All** `IOptions<T>` bindings use the `HCS:Authentication` section — never `Umbraco:*`. Section name constant lives at `PasswordlessOptions.SectionName`.

```json
{
  "HCS": {
    "Authentication": { ... }
  }
}
```

## Dependency rules

- `HCS.Passwordless.Core` has **no dependency** on any add-on package.
- All three add-on packages (`MagicLink`, `Otp`, `WebAuthn`) depend on Core. They must **not** depend on each other.
- The demo may reference all four.

## Registration pattern

Services are wired via `IUmbracoBuilder` extension methods. Each add-on package has one entry-point method; register only the ones you need:

```csharp
builder.CreateUmbracoBuilder()
    .AddPasswordlessMagicLink()  // magic link (MagicLinkBuilderExtensions)
    .AddPasswordlessOtp()        // OTP         (OtpBuilderExtensions)
    .AddPasswordlessWebAuthn()   // WebAuthn    (WebAuthnBuilderExtensions)
    .Build();
```

Each `AddX()` method calls `services.AddPasswordlessCoreOnce()` internally, so core infrastructure is registered exactly once regardless of call order or how many add-ons are installed.

Endpoints are exposed via standard `[ApiController]` MVC controllers and are auto-discovered by Umbraco's pipeline — no explicit endpoint-mapping call is needed in `Program.cs`.

## Key abstractions

| Interface | Purpose |
|-----------|---------|
| `IPasswordlessAuthFactor` | Reports whether an auth method is enabled |
| `ISingleUseTokenStore` | Stores/consumes single-use tokens (distributed cache) |
| `IPasswordlessRateLimiter` | Sliding-window per-IP and per-email rate limiting |
| `IPasswordlessNotificationSender` | Sends magic-link emails |
| `IOtpNotificationSender` | Sends OTP emails |
| `IMemberCredentialStore` | Stores WebAuthn credentials in Umbraco DB |

## Security invariants — do not break

- Token comparison **must** use `ConstantTime.Equals` — never `==` or `string.Equals`.
- All auth endpoints apply `FakeWork` delay regardless of hit/miss.
- `ReturnUrl` must pass `ReturnUrlValidator` before any redirect.
- Magic link and OTP tokens are hashed (SHA-256) before storage.
- Tokens are invalidated on first successful use (`SingleUse` enforced by `ISingleUseTokenStore`).

## Email templates

Default templates ship inside the RCL at `Views/Emails/Passwordless/`. Host projects override by placing templates at the same relative path under their own `/Views/` folder. Templates use strongly-typed models (`MagicLinkEmailModel`, `OtpEmailModel`).

## Target framework / SDK

- All packages: `net8.0`, `Microsoft.NET.Sdk.Razor`
- Umbraco version range: `[13.0, 14.0)` — do not bump to Umbraco 14 without a separate branch/package.

## Packaging

All `src/` projects are packable (`<IsPackable>true</IsPackable>`). Build props live in `Directory.Build.props`; centralized package versions in `Directory.Packages.props`. No floating versions — pin everything.

## What lives in the demo vs. the packages

The demo site (`demo/`) is for interactive verification only — it is not shipped. Any feature that needs to be testable should have a unit test in `tests/`, not just a demo route.
