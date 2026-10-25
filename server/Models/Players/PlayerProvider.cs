/*!************************************************************************
\file   PlayerProvider.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the PlayerProvider enum, the kinds of identity a
        player can have: device, email and steam.

        Used by PlayerIdentity and PlayerIdentityService.
**************************************************************************/
namespace server.Models
{
    public enum PlayerProvider : short
    {
        // a guest's device token, made by register. deleted once they verify an email, since the
        // account is recoverable from then on
        Device = 0,
        Email = 1,
        Steam = 2,
    }
}
