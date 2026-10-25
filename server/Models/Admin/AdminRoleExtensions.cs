namespace server.Models
{
    public static class AdminRoleExtensions
    {
        // the name the api shows for a role
        public static string ToName(this AdminRole role) => role == AdminRole.Owner ? "owner" : "admin";
    }
}
