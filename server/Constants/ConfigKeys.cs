namespace server.Constants
{
    // Names of settings read from configuration. As environment variables the colon becomes
    // a double underscore (Smtp:Host is Smtp__Host).
    public static class ConfigKeys
    {
        public const string SmtpHost = "Smtp:Host";
        public const string SmtpPort = "Smtp:Port";
        public const string SmtpUser = "Smtp:User";
        public const string SmtpPassword = "Smtp:Password";
        public const string SmtpFrom = "Smtp:From";

        // Used when Smtp:Port isn't set. 587 is the standard port for mail submission.
        public const int SmtpDefaultPort = 587;
    }
}
