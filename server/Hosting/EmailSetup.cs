using server.Constants;
using server.SEmail;

namespace server.Hosting
{
    public static class EmailSetup
    {
        // real mail only if an smtp host is set, otherwise just log the message
        public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config[ConfigKeys.SmtpHost]))
                services.AddSingleton<IEmailSender, LogEmailSender>();
            else
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
            return services;
        }
    }
}
