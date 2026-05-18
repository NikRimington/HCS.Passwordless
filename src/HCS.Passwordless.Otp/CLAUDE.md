# HCS.Passwordless.Otp — CLAUDE.md

## Role

Add-on RCL package. Depends on `HCS.Passwordless.Core`. Ships via NuGet. Must not depend on the MagicLink or WebAuthn packages.

## Key namespaces

| Namespace | Contents |
|-----------|---------|
| `Auth` | `OtpAuthFactor` — implements `IPasswordlessAuthFactor` |
| `Configuration` | `OtpOptions`, `OtpOptionsValidator` |
| `DependencyInjection` | `OtpBuilderExtensions` — entry point, chains off `IUmbracoBuilder` |
| `Controllers` | `OtpController` — handles OTP request and verify |
| `Notifications` | `EmailOtpNotificationSender`, `IOtpNotificationSender` |
| `Security.TokenProviders` | `OtpTokenProvider` |
| `Services` | `IOtpCodeStore`, `IAttemptCounter` and their distributed-cache implementations |

## Configuration binding

Options bind from `HCS:Authentication:Otp`. Do not introduce a separate top-level section.

## Security rules

- Max attempt enforcement lives in `IAttemptCounter`. The verify endpoint **must** check and increment the counter before comparing the code.
- Lockout state is stored in distributed cache — the same instance used for token storage. Do not bypass it.
- OTP codes must be stored hashed (SHA-256), same as magic-link tokens.
- Codes are single-use: consume the code store entry immediately on first successful verify, regardless of whether sign-in succeeds.

## Service registration

Entry point is `AddPasswordlessOtp()` on `IUmbracoBuilder`. It calls `services.AddPasswordlessCoreOnce()` internally, so it can be registered independently without needing `AddPasswordlessMagicLink()` first. The `OtpController` is auto-discovered by ASP.NET Core's MVC pipeline — no explicit endpoint mapping call is needed in `Program.cs`.

## Attempt counter semantics

`IAttemptCounter.IncrementAsync` returns the new count. If the count exceeds `OtpOptions.MaxAttempts`, the endpoint must return a lockout response rather than a wrong-code response. The counter is keyed by a combination of email and IP to prevent both targeted and spray attacks.

## Email templates

Views live at `Views/Emails/Passwordless/Otp.cshtml` and `Otp.Text.cshtml` inside the RCL. Model type is `OtpEmailModel`. Host project overrides take precedence.

## What not to change without careful review

- Attempt-counter check in `VerifyOtpEndpoint` — removing or reordering it bypasses brute-force protection.
- Code store consume call — must happen even when sign-in fails so codes cannot be reused after a partial failure.
