# HCS Passwordless for Umbraco

Passwordless member authentication for **Umbraco 13**, distributed as NuGet packages. Three pluggable authentication methods — magic links, one-time passwords (OTP), and WebAuthn/FIDO2 passkeys — can be used individually or together.

## Packages

| Package | Description |
|---------|-------------|
| `HCS.Passwordless.MagicLink` | Magic link authentication |
| `HCS.Passwordless.Otp` | Add-on — email OTP codes |
| `HCS.Passwordless.WebAuthn` | Add-on — FIDO2/passkey authentication |

`HCS.Passwordless.Core` is a shared dependency installed automatically by any of the above.

## Requirements

- .NET 8.0
- Umbraco 13.x
- An existing Umbraco member type

## Quick Start

### 1. Install

```bash
dotnet add package HCS.Passwordless.MagicLink
# optional add-ons:
dotnet add package HCS.Passwordless.Otp
dotnet add package HCS.Passwordless.WebAuthn
```

### 2. Register services (`Program.cs`)

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddPasswordlessMagicLink()  // magic link sign-in
    .AddPasswordlessOtp()        // optional
    .AddPasswordlessWebAuthn()   // optional
    .Build();
```

### 3. Configure (`appsettings.json`)

```json
{
  "HCS": {
    "Authentication": {
      "LoginPath": "/login",
      "PostLoginRedirectPath": "/member",
      "RejectUnknownEmails": false,
      "MagicLink": {
        "Enabled": true,
        "TokenLifespan": "00:15:00",
        "SingleUse": true
      },
      "Notifications": {
        "FromAddress": "noreply@example.com",
        "FromName": "My Site"
      }
    }
  }
}
```

### 4. Add login UI

In your login view, render the built-in partial:

```cshtml
@await Html.PartialAsync("Passwordless/LoginForm")
```

## Authentication Methods

### Magic Links
The core method. The user enters their email; a signed, single-use link is emailed to them. Clicking it signs them in instantly. Tokens are stored in distributed cache and validated server-side.

### OTP (One-Time Password)
An email is sent containing a short numeric code (default 6 digits). The user enters the code on a second screen. Includes configurable attempt limits and lockout.

### WebAuthn / Passkeys
FIDO2 hardware-backed authentication (Touch ID, Face ID, security keys). Requires credential registration before first sign-in. Credentials are stored in the Umbraco database via a migration.

## Security Features

- Constant-time token comparison to prevent timing attacks
- Single-use tokens (invalidated on first use)
- Sliding-window rate limiting — per-IP and per-email
- Fake-work delay on all auth endpoints to flatten timing differences
- `ReturnUrl` validation to prevent open redirects
- SHA-256 token hashing in storage

## Configuration Reference

All settings live under `HCS:Authentication`. See individual package READMEs for full option tables.

## Customising Email Templates

Default templates are Razor partials shipped in the RCL. Override any template by adding a matching path under your host project's `/Views` folder:

- `Views/Emails/Passwordless/MagicLink.cshtml`
- `Views/Emails/Passwordless/MagicLink.Text.cshtml`
- `Views/Emails/Passwordless/Otp.cshtml`
- `Views/Emails/Passwordless/Otp.Text.cshtml`
- `Views/Emails/Passwordless/_Layout.cshtml`

## Project Structure

```
Passwordless.slnx
├── src/
│   ├── HCS.Passwordless.Core         # Shared infrastructure
│   ├── HCS.Passwordless.MagicLink    # Magic link add-on
│   ├── HCS.Passwordless.Otp          # OTP add-on
│   └── HCS.Passwordless.WebAuthn     # WebAuthn add-on
├── tests/
│   └── HCS.Passwordless.Tests        # xUnit test suite
└── demo/
    └── HCS.Passwordless.Demo         # Full demo Umbraco site
```

## Building

```bash
dotnet build
dotnet test
```

## Running the Demo

```bash
cd demo/HCS.Passwordless.Demo
dotnet run
```

Then open `https://localhost:44391` and complete the Umbraco install. The demo exposes `/login` and `/member`.

## License

MIT — Nik Rimington
