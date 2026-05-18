# HCS.Passwordless.WebAuthn — CLAUDE.md

## Role

Add-on RCL package for FIDO2/WebAuthn passkey authentication. Depends on `HCS.Passwordless.Core`. Ships via NuGet. Must not depend on the MagicLink or OTP packages.

## Key namespaces

| Namespace | Contents |
|-----------|---------|
| `Auth` | `WebAuthnAuthFactor` — implements `IPasswordlessAuthFactor` |
| `Composing` | `WebAuthnComposer` — registers WebAuthn Umbraco notifications |
| `Configuration` | `WebAuthnOptions`, `WebAuthnOptionsValidator` |
| `DependencyInjection` | `WebAuthnBuilderExtensions` — entry point |
| `Controllers` | `WebAuthnController` (sign-in + registration), `WebAuthnCredentialsController` (credential management) |
| `WebAuthn.Migrations` | `PasswordlessMigrationPlan`, `AddPasswordlessMemberCredentials` |
| `WebAuthn.Storage` | `IMemberCredentialStore`, `UmbracoDbMemberCredentialStore`, `StoredCredential` |
| `WebAuthn.Services` | `IWebAuthnChallengeStore`, `ChallengeState` |

## Configuration binding

Options bind from `HCS:Authentication:WebAuthn`. Do not add a separate top-level section.

## Security rules

- Challenge state is single-use. The challenge store must invalidate the challenge immediately after it is retrieved for verification.
- Counter regression detection is not optional. When `WebAuthnController` detects a regressed counter, it **must** fire `PasskeyCounterRegressionNotification` before completing sign-in.
- `Origins` validation uses the FIDO2 library's built-in checks — do not bypass or short-circuit them.
- Registration and sign-in challenges must be scoped to the requesting member/session and must not be reusable across members.

## Database

Credentials are stored in a custom Umbraco DB table added by `AddPasswordlessMemberCredentials` migration. The DTO is `MemberCredentialDto`. The migration plan name is `PasswordlessMigrationPlan` — do not rename it without also updating any existing database records.

## Service registration

Entry point is `AddPasswordlessWebAuthn()` on `IUmbracoBuilder`. It calls `services.AddPasswordlessCoreOnce()` internally, so it can be registered independently without needing `AddPasswordlessMagicLink()` first. The controllers are auto-discovered by ASP.NET Core's MVC pipeline — no explicit endpoint mapping call is needed in `Program.cs`. `WebAuthnComposer` handles Umbraco notification wiring.

## FIDO2 library

Uses `Fido2.AspNet` 4.0.1. Key types: `Fido2`, `AssertionOptions`, `CredentialCreateOptions`. Options are constructed per-request with the configured `RpName` and `Origins`. Do not cache `Fido2` instances across requests.

## What not to change without careful review

- Challenge store consume logic — must remain atomic and single-use.
- Counter regression check in `WebAuthnController` — this is a FIDO2 spec requirement.
- Migration plan name — changing it will create duplicate migrations on existing installs.
- Origins validation — must use the FIDO2 library, not custom string comparison.
