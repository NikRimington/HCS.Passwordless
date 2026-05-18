# HCS.Passwordless.WebAuthn

WebAuthn / FIDO2 passkey add-on for `HCS.Passwordless`. Enables hardware-backed biometric or security-key authentication for Umbraco members using the browser's Credential Management API.

## Requirements

- Umbraco 13 (`[13.0, 14.0)`)
- A browser that supports WebAuthn (all modern browsers)
- `HCS.Passwordless.Core` is pulled in automatically as a transitive dependency

## Installation

```bash
dotnet add package HCS.Passwordless.WebAuthn
```

## Setup

### 1. Register (`Program.cs`)

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddPasswordlessWebAuthn()
    .Build();
```

### 2. Configure (`appsettings.json`)

```json
{
  "HCS": {
    "Authentication": {
      "WebAuthn": {
        "Enabled": true,
        "RpName": "My Site",
        "Origins": [ "https://example.com" ]
      }
    }
  }
}
```

> **Important:** `Origins` must exactly match the origin of the site as seen by the browser, including scheme and port.

### 3. Add passkey UI partials

```cshtml
@* Sign-in *@
@await Html.PartialAsync("Passwordless/PasskeySignInButton")

@* On a member profile/settings page: *@
@await Html.PartialAsync("Passwordless/PasskeyRegisterButton")
@await Html.PartialAsync("Passwordless/PasskeyCredentialList")
```

## Configuration Options

### `HCS:Authentication:WebAuthn`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Enable/disable WebAuthn flow |
| `RpName` | string | — | Relying party display name shown to the user |
| `Origins` | string[] | `[]` | Allowed origins (must match browser origin exactly) |

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/auth/webauthn/register/options` | Get registration challenge |
| `POST` | `/auth/webauthn/register/complete` | Complete credential registration |
| `POST` | `/auth/webauthn/signin/options` | Get authentication challenge |
| `POST` | `/auth/webauthn/signin/complete` | Complete authentication and sign in |
| `GET` | `/auth/webauthn/credentials` | List member's registered credentials |
| `PATCH` | `/auth/webauthn/credentials/{id}` | Rename a credential |
| `DELETE` | `/auth/webauthn/credentials/{id}` | Remove a credential |

## Database Migration

The package adds a `MemberCredential` table to the Umbraco database via Umbraco's migration system. This runs automatically on startup.

## Security Notes

- Challenge state is stored in distributed cache and is single-use.
- Counter regression (a credential reporting a lower sign-count than previously recorded) triggers a `PasskeyCounterRegressionNotification` so the site can alert the member of a potential cloned authenticator.
- WebAuthn requires HTTPS in production. `localhost` is allowed for development.

## Replacing Services

| Interface | Default | Purpose |
|-----------|---------|---------|
| `IWebAuthnChallengeStore` | Distributed cache implementation | Store WebAuthn challenges |
| `IMemberCredentialStore` | `UmbracoDbMemberCredentialStore` | Persist credentials in Umbraco DB |
