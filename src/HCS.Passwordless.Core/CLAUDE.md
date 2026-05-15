# HCS.Passwordless.Core — CLAUDE.md

## Role

Shared infrastructure package. All add-on packages (`MagicLink`, `Otp`, `WebAuthn`) depend on this. Has no dependency on any add-on. Ships via NuGet. Uses `Microsoft.NET.Sdk.Razor` (RCL) to serve the shared `passwordless.js` client script at `App_Plugins/HCS.Passwordless/js/passwordless.js`. Ships no Razor views — only the static asset.

## Key namespaces

| Namespace | Contents |
|-----------|---------|
| `Auth` | `IPasswordlessAuthFactor` |
| `Configuration` | `PasswordlessOptions`, `PasswordlessOptionsValidator`, `RateLimitOptions`, `NotificationOptions`, `BrandingOptions` |
| `DependencyInjection` | `PasswordlessCoreServiceCollectionExtensions` — `AddPasswordlessCoreOnce()` |
| `Notifications` | `RazorViewRenderer`, `IRazorViewRenderer` |
| `RateLimiting` | `SlidingWindowRateLimiter`, `IPasswordlessRateLimiter` |
| `Security` | `ConstantTime`, `Sha256`, `FakeWork`, `ReturnUrlValidator` |
| `Services` | `ISingleUseTokenStore`, `DistributedCacheSingleUseTokenStore`, `IMemberLookupService`, `IPasswordlessSignInService`, `IPasswordlessClock` |

## Idempotent registration

`AddPasswordlessCoreOnce()` uses a private `CoreServicesMarker` sentinel: if the marker is already registered, it returns immediately. This means any add-on can call it at the start of its own `AddX()` without caring whether another add-on already did.

```csharp
// Each add-on calls this at the top of its registration method
services.AddPasswordlessCoreOnce();
```

## Configuration binding

Shared options bind from `HCS:Authentication`. Section name constant is `PasswordlessOptions.SectionName`. Never use `Umbraco:*`.

## InternalsVisibleTo

The csproj grants internal visibility to `HCS.Passwordless.MagicLink`, `.Otp`, `.WebAuthn`, and `.Tests` so add-ons can use internal helpers without making them public API.

## What not to change without careful review

- `ConstantTime.cs` — any change is a security regression risk.
- `FakeWork.cs` — removing the await on the error path removes timing protection.
- `DistributedCacheSingleUseTokenStore` — the consume operation must remain atomic.
- `ReturnUrlValidator` — all allowlist/denylist logic must stay in sync with tests.
- `AddPasswordlessCoreOnce` marker pattern — never call `AddSingleton<CoreServicesMarker>()` outside this method.
