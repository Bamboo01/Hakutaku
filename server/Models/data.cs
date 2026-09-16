// This is just sample code, feel free to overwrite it.

using Microsoft.EntityFrameworkCore;

namespace server.Models
{
    // Create a POD struct
    public class Player
    {
        public Guid Id { get; set; }
        public string DeviceId { get; set; } = "";
        public int Xp { get; set; }
    }

    public class Db : DbContext
    {
        public Db(DbContextOptions<Db> options) : base(options) { }
        
        // C# magic, serializes a POD struct into a database set
        public DbSet<Player> Players => Set<Player>();
    }
}
