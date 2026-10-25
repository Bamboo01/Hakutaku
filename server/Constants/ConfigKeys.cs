/*!************************************************************************
\file   ConfigKeys.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the names of the settings read from configuration,
        such as the owner password and the SMTP settings. As environment
        variables the colon becomes a double underscore, so Smtp:Host is
        Smtp__Host.

        Read by OwnerSeeder, EmailSetup and SmtpEmailSender.
**************************************************************************/
namespace server.Constants
{
    // names of settings read from config. as env vars the colon becomes a double underscore
    // (Smtp:Host is Smtp__Host)
    public static class ConfigKeys
    {
        // owner account's password on first start. unset means one is generated and logged
        public const string AdminPassword = "HAKUTAKU_ADMIN_PASSWORD";

        public const string SmtpHost = "Smtp:Host";
        public const string SmtpPort = "Smtp:Port";
        public const string SmtpUser = "Smtp:User";
        public const string SmtpPassword = "Smtp:Password";
        public const string SmtpFrom = "Smtp:From";

        // used when Smtp:Port isn't set. 587 is the standard mail submission port
        public const int SmtpDefaultPort = 587;
    }
}
