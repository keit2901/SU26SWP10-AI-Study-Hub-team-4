namespace AI_Study_Hub_v2.Options;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public bool Enabled { get; set; }

    public bool AllowDevelopmentFallback { get; set; } = true;

    public string SiteKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string VerifyEndpoint { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public string Theme { get; set; } = "light";

    public string Size { get; set; } = "flexible";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SiteKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(VerifyEndpoint);
}
