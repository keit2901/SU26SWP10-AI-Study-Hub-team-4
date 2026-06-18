namespace AI_Study_Hub_v2.Options;

public sealed class RecaptchaOptions
{
    public const string SectionName = "Recaptcha";
    public bool Enabled { get; set; }
    public bool AllowDevelopmentFallback { get; set; } = true;
    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string VerifyEndpoint { get; set; } = "https://www.google.com/recaptcha/api/siteverify";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}
