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
