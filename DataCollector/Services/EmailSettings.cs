namespace DataCollector;

public static class EmailSettings
{
    public const string RecipientEmail = "recipient@example.com";

    // TODO: Uzupelnij dane SMTP dla konta nadawcy.
    public const string SenderEmail = "sender@example.com";
    public const string SmtpHost = "smtp.gmail.com";
    public const int SmtpPort = 587;
    public const string SmtpUser = "sender@example.com";
    public const string SmtpPassword = "APP_PASSWORD";
    public const bool UseStartTls = true;

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SenderEmail) &&
        !string.IsNullOrWhiteSpace(SmtpHost) &&
        !string.IsNullOrWhiteSpace(SmtpUser) &&
        !string.IsNullOrWhiteSpace(SmtpPassword) &&
        !SenderEmail.StartsWith("sender@", StringComparison.OrdinalIgnoreCase) &&
        !SmtpPassword.StartsWith("APP_", StringComparison.OrdinalIgnoreCase);
}
