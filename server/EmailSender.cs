using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace server
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body);
    }

    // Used when no SMTP server is configured (local dev). Writes the whole message to
    // the log instead, codes included -- fine on a dev machine, which is the only place
    // this should be running. Set Smtp:Host to send real mail.
    public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body)
        {
            logger.LogWarning("No SMTP server configured, so this email was not sent. To: {To}, Subject: {Subject}\n{Body}", to, subject, body);
            return Task.CompletedTask;
        }
    }

    // Configured with Smtp:Host, Smtp:Port, Smtp:User, Smtp:Password and Smtp:From
    // (environment variables use a double underscore: Smtp__Host).
    public class SmtpEmailSender(IConfiguration config) : IEmailSender
    {
        public async Task SendAsync(string to, string subject, string body)
        {
            var host = config["Smtp:Host"]!;
            var port = config.GetValue("Smtp:Port", 587);
            var user = config["Smtp:User"];
            var from = config["Smtp:From"] ?? user ?? throw new InvalidOperationException("Smtp:From is not set");

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            client.Timeout = 15_000;
            // 465 is TLS from the first byte; anything else (587) upgrades with STARTTLS,
            // which fails rather than sending in the clear if the server won't do it.
            await client.ConnectAsync(host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, config["Smtp:Password"] ?? "");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
