namespace server.Models
{
    // A Player can have multiple characters. No independent progress/stats of
    // their own yet -- that's tracked on Player -- so this is just identity.
    public class Character
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public Player Player { get; set; } = null!;
        public string Name { get; set; } = "";
    }
}
