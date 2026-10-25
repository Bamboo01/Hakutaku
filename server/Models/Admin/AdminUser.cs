/*!************************************************************************
\file   AdminUser.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the AdminUser model: an admin account with a
        unique username, an optional email, the Argon2id password hash and a
        role. Deactivated admins are kept, with a time marking when.

        Table mapped in Db.cs. Sessions refer to it through AdminSession.
**************************************************************************/
namespace server.Models
{
    public class AdminUser
    {
        public long Id { get; set; }
        // what you log in with. unique, ignoring case
        public string Username { get; set; } = "";
        // optional. not used for login, just somewhere to send report hooks later
        public string? Email { get; set; }
        // argon2id string, the salt and settings are inside it
        public string PwHash { get; set; } = "";
        public AdminRole Role { get; set; }
        public byte[]? TotpSecret { get; set; }
        public DateTime CreatedAt { get; set; }
        public long? CreatedBy { get; set; }
        public DateTime? DisabledAt { get; set; }
    }
}
