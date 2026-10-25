/*!************************************************************************
\file   Player.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the Player model, a player account. How a player
        proves who they are lives in PlayerIdentity, not on this row. The
        display name is an optional label and is not unique.

        Related to PlayerIdentity, PlayerSession, PlayerEmailCode, Character
        and TelemetryEvent. Table mapped in Db.cs.
**************************************************************************/
namespace server.Models
{
    // a player account. how they prove who they are lives in PlayerIdentity (device token,
    // email...), not on this row
    public class Player
    {
        public Guid Id { get; set; }
        public int Xp { get; set; }
        // what the game shows for this player. optional and not unique, it's a label and not a way to
        // sign in, so two players can both be "Bob". see PlayerInputRules.TryNormalizeDisplayName
        public string? DisplayName { get; set; }
        // the hardware id the game sent at register (e.g. unity's SystemInfo.deviceUniqueIdentifier).
        // only for support and spotting reroll farms. it isn't secret so nothing looks a player up or signs
        // one in by it. kept here and not on the device identity, which gets deleted when the email is verified
        public string? RegisteredHardwareId { get; set; }
    }
}
