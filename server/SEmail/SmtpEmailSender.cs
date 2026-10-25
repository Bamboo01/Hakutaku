using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using server.Constants;

namespace server.SEmail
{
    // settings: Smtp:Host, Smtp:Port, Smtp:User, Smtp:Password, Smtp:From
    // (as env vars they use a double underscore, e.g. Smtp__Host)
    public class SmtpEmailSender(IConfiguration config) : IEmailSender
    {
        public async Task SendAsync(string to, string subject, string body)
        {
            var host = config[ConfigKeys.SmtpHost]!;
            var port = config.GetValue(ConfigKeys.SmtpPort, ConfigKeys.SmtpDefaultPort);
            var user = config[ConfigKeys.SmtpUser];
            var from = config[ConfigKeys.SmtpFrom] ?? user ?? throw new InvalidOperationException(ConfigKeys.SmtpFrom + " is not set");

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            client.Timeout = 15_000;
            // 465 is tls from the first byte, anything else (587) upgrades with starttls, which fails
            // rather than sending in the clear if the server won't do it
            await client.ConnectAsync(host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, config[ConfigKeys.SmtpPassword] ?? "");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
