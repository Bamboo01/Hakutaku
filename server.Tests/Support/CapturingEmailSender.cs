/*!************************************************************************
\file   CapturingEmailSender.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   31st October 2026
\brief  This file defines CapturingEmailSender, a fake IEmailSender for the
        integration tests. It keeps every mail instead of sending it, so a
        test can read the 6-digit code out of a mail or check that no mail
        was sent.

        Put in place by HakutakuFactory.
**************************************************************************/
using System.Text.RegularExpressions;
using server.SEmail;

namespace server.Tests.Support
{
    public record SentMail(string To, string Subject, string Body);

    public class CapturingEmailSender : IEmailSender
    {
        readonly List<SentMail> sent = [];

        public Task SendAsync(string to, string subject, string body)
        {
            lock (sent) sent.Add(new SentMail(to, subject, body));
            return Task.CompletedTask;
        }

        public List<SentMail> To(string address)
        {
            lock (sent) return sent.Where(m => m.To == address).ToList();
        }

        // the 6-digit code in the newest mail to an address whose subject contains the given text
        public string? LastCode(string address, string subjectContains)
        {
            var mail = To(address).LastOrDefault(m => m.Subject.Contains(subjectContains));
            var match = mail is null ? null : Regex.Match(mail.Body, @"\b(\d{6})\b");
            return match is { Success: true } ? match.Groups[1].Value : null;
        }

        // some mails go out from a background task, so a test waits for them instead of checking at once
        public static async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 3000)
        {
            for (var waited = 0; waited < milliseconds; waited += 50)
            {
                if (condition()) return true;
                await Task.Delay(50);
            }
            return condition();
        }
    }
}
