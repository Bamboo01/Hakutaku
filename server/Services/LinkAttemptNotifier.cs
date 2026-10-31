/*!************************************************************************
\file   LinkAttemptNotifier.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines LinkAttemptNotifier. When someone tries to link an
        email that already belongs to an account, it mails the real owner a
        heads-up, at most once per address per hour. It is a singleton
        because the memory of who was already told has to outlive a request.

        Used by LinkEmail.cs. Sends through IEmailSender.
**************************************************************************/
using Microsoft.Extensions.Caching.Memory;
using server.SEmail;

namespace server.Services
{
    // when someone tries to link an email that already belongs to an account, the caller gets the
    // same answer as a real link (so they can't tell the email is taken) and the real owner gets
    // a heads-up instead. a singleton because the "already told them" memory has to outlive a request
    public class LinkAttemptNotifier(IMemoryCache cache, IEmailSender mail, ILogger<LinkAttemptNotifier> logger)
    {
        // one heads-up per address per hour, so this can't be used to spam someone's inbox. it's kept in
        // memory, so a restart forgets it, which is fine for a courtesy mail
        static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

        // sent in the background so the caller's response time doesn't depend on the mail server
        // takes the address's key (see PlayerInputRules.EmailKey), which for gmail is the plain address,
        // and a mail to that reaches the same inbox as any alias of it
        public void NotifyOwner(string email)
        {
            var key = $"link-attempt:{email}";
            if (cache.TryGetValue(key, out _)) return;
            cache.Set(key, true, Cooldown);

            _ = Task.Run(async () =>
            {
                try
                {
                    await mail.SendAsync(email, "Hakutaku: someone tried to use your email",
                        "Someone just tried to add this email address to a Hakutaku player account, but it's already " +
                        "linked to yours, so nothing changed.\n\nIf that was you, for example on a second device, you " +
                        "can ignore this. If it wasn't you, you don't need to do anything: your account is untouched.");
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not send the link-attempt email");
                }
            });
        }
    }
}
