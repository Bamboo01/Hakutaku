/*!************************************************************************
\file   IEmailSender.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines IEmailSender, the one-method interface for sending
        an email, so the code that needs to send mail does not care how it
        is sent.

        Implemented by SmtpEmailSender and LogEmailSender. Chosen in
        EmailSetup.cs.
**************************************************************************/
namespace server.SEmail
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body);
    }
}
