using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DataCollector;

public class EmailSender
{
    public async Task<bool> SendCsvAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            return false;
        }

        if (!EmailSettings.IsConfigured)
        {
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(EmailSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(EmailSettings.RecipientEmail));
        message.Subject = $"Log CSV {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        var builder = new BodyBuilder
        {
            TextBody = "Log CSV z ostatniego okresu w zalaczniku."
        };
        builder.Attachments.Add(csvPath);
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        client.ServerCertificateValidationCallback = ValidateServerCertificate;

        var socket = EmailSettings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(EmailSettings.SmtpHost, EmailSettings.SmtpPort, socket, cancellationToken);
        await client.AuthenticateAsync(EmailSettings.SmtpUser, EmailSettings.SmtpPassword, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        return true;
    }

    private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        if (sslPolicyErrors != SslPolicyErrors.RemoteCertificateChainErrors || chain == null)
        {
            return false;
        }

        foreach (var status in chain.ChainStatus)
        {
            if (status.Status == X509ChainStatusFlags.RevocationStatusUnknown ||
                status.Status == X509ChainStatusFlags.OfflineRevocation)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
