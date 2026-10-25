/*!************************************************************************
\file   Character.cs
\author Choi Meng Yew
\par    choi.m@digipen.edu
\date   25th October 2026
\brief  This file defines the Character model. A player can have several
        characters, and for now a character is only an identity, since
        progress and stats live on Player.

        Belongs to a Player. Table mapped in Db.cs.
**************************************************************************/
namespace server.Models
{
    // a player can have several characters. no progress or stats of their own yet (that's on
    // Player), so for now it's just an identity
    public class Character
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string Name { get; set; } = "";
    }
}
