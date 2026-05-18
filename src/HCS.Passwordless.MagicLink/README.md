# HCS.Passwordless.MagicLink

Magic link sign-in for Umbraco 13 members. Part of the HCS Passwordless suite. Provides email-based one-click authentication with built-in rate limiting, single-use tokens, and a branded email notification system.

## Installation

```bash
dotnet add package HCS.Passwordless.MagicLink
```

## Setup

### 1. Register (`Program.cs`)

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddPasswordlessMagicLink()
    .Build();
```

### 2. Map endpoints (`Program.cs`)

```csharp
app.MapPasswordlessMembers();
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
        "TokenLifespan": "00:15:00"
      },
      "RateLimits": {
        "PerIpRequestsPerMinute": 10,
        "PerEmailRequestsPerHour": 5,
        "VerifyPerIpPerMinute": 20,
        "FakeWorkDelay": "00:00:00.250"
      },
      "Notifications": {
        "FromAddress": "noreply@example.com",
        "FromName": "My Site",
        "MagicLinkSubject": "Your sign-in link",
        "Branding": {
          "ProductName": "My Site",
          "AccentColor": "#2d6cdf"
        }
      }
    }
  }
}
```

### 4. Login view

```cshtml
@await Html.PartialAsync("Passwordless/LoginForm")
@await Html.PartialAsync("Passwordless/MagicLinkLanding")
```

## Configuration Options

### `HCS:Authentication`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `LoginPath` | string | `/login` | URL of the login page |
| `PostLoginRedirectPath` | string | `/` | Redirect after successful sign-in |
| `RejectUnknownEmails` | bool | `false` | Return an error for unrecognised emails (vs. silent success) |

### `HCS:Authentication:MagicLink`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `true` | Enable/disable magic link flow |
| `TokenLifespan` | TimeSpan | `00:15:00` | How long a link remains valid |

### `HCS:Authentication:RateLimits`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `PerIpRequestsPerMinute` | int | `10` | Max auth requests per IP per minute |
| `PerEmailRequestsPerHour` | int | `5` | Max requests per email address per hour |
| `VerifyPerIpPerMinute` | int | `20` | Max verify attempts per IP per minute |
| `FakeWorkDelay` | TimeSpan | `00:00:00.250` | Minimum response time (timing attack mitigation) |

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/auth/magic-link/request` | Request a magic link email |
| `GET` | `/auth/magic-link/verify` | Show sign-in confirmation page |
| `POST` | `/auth/magic-link/verify` | Validate token and sign in |

## Customising Email Templates

Place overriding views in your host project under the same paths:

```
Views/
  Emails/
    Passwordless/
      MagicLink.cshtml        # HTML email
      MagicLink.Text.cshtml   # Plain-text email
      _Layout.cshtml          # Shared email layout
  Shared/
    Passwordless/
      LoginForm.cshtml        # Login form partial
      MagicLinkLanding.cshtml # "Check your email" page partial
```

## Replacing Services

All services are registered with `TryAdd*`, so you can replace any of them in DI before calling `AddPasswordlessMagicLink`:

```csharp
services.AddScoped<IPasswordlessNotificationSender, MyCustomSender>();
```

Key replaceable services:

| Interface | Default implementation | Purpose |
|-----------|----------------------|---------|
| `IPasswordlessNotificationSender` | `EmailNotificationSender` | Send magic link emails |
| `ISingleUseTokenStore` | `DistributedCacheSingleUseTokenStore` | Token persistence |
| `IPasswordlessRateLimiter` | `SlidingWindowRateLimiter` | Rate limiting |
| `IMemberLookupService` | `MemberLookupService` | Resolve member by email |
| `IPasswordlessSignInService` | `PasswordlessSignInService` | Sign in member |
