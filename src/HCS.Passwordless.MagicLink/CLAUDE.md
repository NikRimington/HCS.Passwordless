# HCS.Passwordless.MagicLink — CLAUDE.md

## Role

Add-on RCL package providing magic link sign-in for Umbraco members. Depends on `HCS.Passwordless.Core`. Ships via NuGet. Must not depend on the OTP or WebAuthn add-ons.

## Key namespaces

| Namespace | Contents |
|-----------|---------|
| `Auth` | `MagicLinkAuthFactor` — implements `IPasswordlessAuthFactor` |
| `Configuration` | `MagicLinkOptions`, `MagicLinkOptionsValidator` |
| `DependencyInjection` | `MagicLinkBuilderExtensions` — the public entry point |
| `Controllers` | `MagicLinkController` — handles magic link request and verify |
| `Notifications` | `EmailNotificationSender`, `IPasswordlessNotificationSender` |
| `Security` | `MagicLinkTokenProvider` |

Shared infrastructure (`ISingleUseTokenStore`, `IPasswordlessRateLimiter`, `IMemberLookupService`, etc.) lives in `HCS.Passwordless.Core`.

## Configuration binding

Magic link options bind from `HCS:Authentication:MagicLink`. Section name constant is `MagicLinkOptions.SectionName`. Never use `Umbraco:*`.

## Critical security rules

- **Always** use `ConstantTime.Equals` for token comparison. Avoid `==` on token strings.
- `FakeWork` must be awaited on both the happy path and error path so timing is uniform.
- `ReturnUrlValidator` must validate every redirect target before issuing a `Location` header.
- Tokens are SHA-256 hashed before storage — store the hash, compare the hash.
- `ISingleUseTokenStore.TryMarkUsedAsync` must be atomic — if it returns false, deny access.

## Service registration

Entry point is `AddPasswordlessMagicLink()` on `IUmbracoBuilder`. It calls `services.AddPasswordlessCoreOnce()` first to register shared infrastructure idempotently, then registers magic-link-specific services with `TryAdd*` so host projects can override.

## Token provider

`MagicLinkTokenProvider` is registered as an ASP.NET Core Identity token provider under the name `TokenProviderNames.MagicLink`. Its lifespan is configured from `MagicLinkOptions.TokenLifespan` at startup, not at runtime.

## Email templates

Views shipped inside the RCL at `Views/Emails/Passwordless/` and `Views/Shared/Passwordless/`. Host overrides take precedence because Umbraco's view engine checks the host project first. Use `RazorViewRenderer` (from Core) to render views to strings.

## What not to change without careful review

- `ConstantTime.cs` (in Core) — any change here is a security regression risk.
- `FakeWork.cs` (in Core) — removing the await on the error path removes timing protection.
- `DistributedCacheSingleUseTokenStore` (in Core) — the consume operation must remain atomic.
- `ReturnUrlValidator` (in Core) — all allowlist/denylist logic must stay in sync with tests.
