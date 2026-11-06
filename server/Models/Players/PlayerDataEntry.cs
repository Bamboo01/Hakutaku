/*!************************************************************************
\file   PlayerDataEntry.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   2nd November 2026
\brief  This file defines the PlayerDataEntry model: one key and its value
        in a player's data, like a PlayFab player data entry. The tier says
        who may change it. Values are plain text, so a game server that
        wants a number or some JSON writes it as a string and reads it back
        the same way.

        Belongs to a Player and goes when the player goes. Table mapped in
        Db.cs. Read and written by PlayerDataService.
**************************************************************************/
namespace server.Models
{
    public class PlayerDataEntry
    {
        public Guid PlayerId { get; set; }
        public PlayerDataTier Tier { get; set; }
        // letters, digits and _ . - only, up to 64 characters. see PlayerDataRules
        public string Key { get; set; } = "";
        // plain text, up to 16 KB. not jsonb on purpose, the game decides what is in it
        public string Value { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
    }
}
