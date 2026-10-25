/*!************************************************************************
\file   AdminRoleExtensions.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines ToName, which gives the name the API shows for an
        AdminRole ("owner" or "admin").

        Extends AdminRole.
**************************************************************************/
namespace server.Models
{
    public static class AdminRoleExtensions
    {
        // the name the api shows for a role
        public static string ToName(this AdminRole role) => role == AdminRole.Owner ? "owner" : "admin";
    }
}
