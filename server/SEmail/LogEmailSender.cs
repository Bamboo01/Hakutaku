/*!************************************************************************
\file   LogEmailSender.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines LogEmailSender, used when no SMTP server is set up
        (local dev). It logs the whole message instead, verification code
        included, which is fine on a dev machine and nowhere else.

        Implements IEmailSender. Chosen in EmailSetup.cs when Smtp:Host is
        not set.
**************************************************************************/
namespace server.SEmail
{
    // used when no smtp server is set up (local dev). logs the whole message instead, code included,
    // which is fine on a dev machine and nowhere else. set Smtp:Host to send real mail
    public class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body)
        {
            logger.LogWarning("No SMTP server configured, so this email was not sent. To: {To}, Subject: {Subject}\n{Body}", to, subject, body);
            return Task.CompletedTask;
        }
    }
}
