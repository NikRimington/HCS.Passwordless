namespace HCS.Passwordless.Otp.Configuration;

public sealed class OtpOptions
{
    public const string SectionName = "HCS:Authentication:Otp";

    public bool Enabled { get; set; } = true;
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromMinutes(5);
    public int CodeLength { get; set; } = 6;
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
    public bool ShowMemberNotFound { get; set; } = false;

    public string NotificationSubject { get; set; } = "Your sign-in code";
    public string NotificationPartial { get; set; } = "Emails/Passwordless/Otp";
    public string NotificationTextPartial { get; set; } = "Emails/Passwordless/Otp.Text";
}
