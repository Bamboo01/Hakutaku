using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace server.Services
{
    // how to spot the two postgres errors we handle on purpose, so a handler can answer
    // 400 or 409 instead of letting them turn into a 500
    public static class DbErrors
    {
        // the insert broke a unique index, e.g. someone took the email or username first
        public static bool IsUniqueViolation(DbUpdateException e) =>
            e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        // the row points at something that doesn't exist, e.g. a playerId with no player
        public static bool IsForeignKeyViolation(DbUpdateException e) =>
            e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
    }
}
