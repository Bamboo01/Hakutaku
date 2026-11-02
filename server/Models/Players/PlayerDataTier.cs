/*!************************************************************************
\file   PlayerDataTier.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the PlayerDataTier enum, which says who may change
        a piece of player data: the game client, only the game server, or
        only the backend itself. It is stored as a small number, so the
        values must never be renumbered.

        Used by PlayerDataEntry and the player data routes.
**************************************************************************/
namespace server.Models
{
    // stored as a smallint with a check constraint (ck_player_data_tier), like PlayerProvider
    public enum PlayerDataTier : short
    {
        // the game client may write these directly
        ClientWritable = 0,

        // the client can read these but only a game server writes them. xp lives here
        ReadOnly = 1,

        // never shown to the client, only the backend reads and writes them
        Internal = 2,
    }
}
