# HCS.Passwordless.Otp

OTP (One-Time Password) add-on for `HCS.Passwordless`. Delivers a short numeric code by email that the member enters to sign in.

## Requirements

- Umbraco 17.3.5 or later (`[17.3.5, 18.0.0)`)
- `HCS.Passwordless.Core` is pulled in automatically as a transitive dependency

## Installation

```bash
dotnet add package HCS.Passwordless.Otp
```

## Setup

### 1. Register (`Program.cs`)

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddPasswordlessOtp()
    .Build();
```

### 2. Configure (`appsettings.json`)

```json
{
  "HCS": {
    "Authentication": {
      "Otp": {
        "Enabled": true,
        "TokenLifespan": "00:05:00",
        "CodeLength": 6,
        "MaxAttempts": 5,
        "LockoutDuration": "00:15:00",
        "NotificationSubject": "Your sign-in code",
        "NotificationPartial": "Emails/Passwordless/Otp",
        "ShowMemberNotFound": false
      }
    }
  }
}
```

### 3. OTP form partial

```cshtml
@await Html.PartialAsync("Passwordless/OtpForm")
```

## Configuration Options

### `HCS:Authentication:Otp`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `true` | Enable/disable OTP flow |
| `TokenLifespan` | TimeSpan | `00:05:00` | How long a code is valid |
| `CodeLength` | int | `6` | Number of digits in the code |
| `MaxAttempts` | int | `5` | Failed attempts before lockout |
| `LockoutDuration` | TimeSpan | `00:15:00` | How long the lockout lasts |
| `NotificationSubject` | string | `Your sign-in code` | Email subject line |
| `NotificationPartial` | string | `Emails/Passwordless/Otp` | Razor partial path for the OTP email body |
| `ShowMemberNotFound` | bool | `false` | For security, hide members not found with supplied email address, otherwise it will let the user know the email was not linked to a member  |

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/auth/otp/request` | Request an OTP code email |
| `POST` | `/auth/otp/verify` | Submit a code to sign in |

## Email Templates

Override the default templates in your host project:

```
Views/
  Emails/
    Passwordless/
      Otp.cshtml        # HTML email
      Otp.Text.cshtml   # Plain-text email
```

## Replacing Services

| Interface | Default | Purpose |
|-----------|---------|---------|
| `IOtpNotificationSender` | `EmailOtpNotificationSender` | Send OTP emails |
| `IOtpCodeStore` | `DistributedCacheOtpCodeStore` | Store OTP codes |
| `IAttemptCounter` | `InMemoryAttemptCounter` | Track failed attempts |
