/*!************************************************************************
\file   EmailSetup.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines AddEmailSender, which picks how email is sent:
        real SMTP when Smtp:Host is set, otherwise a sender that only logs
        the message.

        Called from Program.cs. Chooses between SmtpEmailSender and
        LogEmailSender.
**************************************************************************/
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
