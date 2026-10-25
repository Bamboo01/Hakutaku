using System.Security.Cryptography;
using System.Text;

namespace server.Auth
{
    // sha-256 of a token or code. session tokens, device tokens and emailed codes are all
    // stored like this, so a copy of the db can't be used to log in as anyone
    public static class TokenHasher
    {
        public static byte[] Hash(string token) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
