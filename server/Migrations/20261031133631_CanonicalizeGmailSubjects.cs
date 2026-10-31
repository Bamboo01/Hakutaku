using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <summary>
    /// Gmail ignores dots and anything after a + in the part before the @, and googlemail.com is
    /// the same inbox as gmail.com, so email identities are now matched by a key that folds those
    /// away (see PlayerInputRules.EmailKey). The key is stored in subject, and email keeps the
    /// address as typed. This brings existing Gmail rows in line so nobody is locked out.
    ///
    /// If two existing rows would fold to the same key, only the oldest is rewritten and the other
    /// keeps its old subject, so the primary key can never be violated and startup can't fail on it.
    /// </summary>
    public partial class CanonicalizeGmailSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // provider 1 is email. subject was the lowercased address, so it can be folded directly
            migrationBuilder.Sql("""
                UPDATE player_identities p
                SET subject = pick.canon
                FROM (
                    SELECT DISTINCT ON (c.canon) c.subject, c.canon
                    FROM (
                        SELECT subject, linked_at,
                               replace(split_part(split_part(subject, '@', 1), '+', 1), '.', '') || '@gmail.com' AS canon
                        FROM player_identities
                        WHERE provider = 1 AND split_part(subject, '@', 2) IN ('gmail.com', 'googlemail.com')
                    ) c
                    WHERE c.canon <> c.subject
                      AND c.canon <> '@gmail.com'
                      AND NOT EXISTS (SELECT 1 FROM player_identities o WHERE o.provider = 1 AND o.subject = c.canon)
                    ORDER BY c.canon, c.linked_at
                ) pick
                WHERE p.provider = 1 AND p.subject = pick.subject;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the address as typed is still in email, so subject can go back to it
            migrationBuilder.Sql("""
                UPDATE player_identities p
                SET subject = p.email
                WHERE p.provider = 1 AND p.email IS NOT NULL AND p.subject <> p.email
                  AND NOT EXISTS (SELECT 1 FROM player_identities o WHERE o.provider = 1 AND o.subject = p.email);
                """);
        }
    }
}
