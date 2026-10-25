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
