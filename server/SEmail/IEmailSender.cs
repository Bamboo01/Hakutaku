namespace server.SEmail
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body);
    }
}
