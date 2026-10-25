using server.Models;

namespace server.Endpoints.Characters
{
    // what the api sends back for a character: just the fields a client needs, not the whole
    // db entity (which would also carry the nested player)
    public record CharacterResponse(Guid Id, Guid PlayerId, string Name)
    {
        public static CharacterResponse From(Character c) => new(c.Id, c.PlayerId, c.Name);
    }
}
