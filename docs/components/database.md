# Database

PostgreSQL 18, reached through EF Core 10 with the Npgsql provider. The schema
lives in version control as C# migrations, and the app applies them on startup.

## The schema

```mermaid
erDiagram
    Players ||--o{ Characters : "owns"
    Players ||--o{ TelemetryEvents : "emits"
    Players ||--o{ player_identities : "proves identity with"
    Players ||--o{ player_sessions : "has"
    Players ||--o{ player_email_codes : "is mailed"
    Players ||--o{ player_data : "has"
    admin_users ||--o{ admin_sessions : "has"
    admin_users ||--o{ admin_users : "created_by"

    Players {
        uuid Id PK
        text DisplayName
        text RegisteredHardwareId
    }
    player_identities {
        smallint provider PK
        text subject PK
        uuid player_id FK
        text email
        text pw_hash
        timestamp linked_at
        timestamp verified_at
    }
    player_sessions {
        bigint id PK
        uuid player_id FK
        bytea token_hash
        timestamp issued_at
        timestamp expires_at
        timestamp revoked_at
    }
    player_email_codes {
        bigint id PK
        uuid player_id FK
        smallint purpose
        bytea code_hash
        timestamp created_at
        timestamp expires_at
        int attempts
        timestamp used_at
    }
    Characters {
        uuid Id PK
        uuid PlayerId FK
        text Name
    }
    TelemetryEvents {
        uuid Id PK
        uuid PlayerId FK
        text EventType
        timestamp Timestamp
        text Data
    }
    admin_users {
        bigint id PK
        text username
        text email
        text pw_hash
        smallint role
        bytea totp_secret
        timestamp created_at
        bigint created_by FK
        timestamp disabled_at
    }
    admin_sessions {
        bigint id PK
        bigint admin_id FK
        bytea token_hash
        inet ip
        timestamp issued_at
        timestamp expires_at
        timestamp idle_expires_at
        timestamp revoked_at
    }
    player_data {
        uuid player_id PK
        smallint tier PK
        text key PK
        text value
        timestamptz updated_at
    }
    server_keys {
        bigint id PK
        text name
        bytea key_sha256 UK
        timestamptz created_at
        timestamptz expires_at
        timestamptz revoked_at
        timestamptz last_used_at
    }
```

## Two naming conventions, on purpose

| | Game data | Admin and player-auth data |
|---|---|---|
| Tables | `Players`, `Characters`, `TelemetryEvents` | `admin_users`, `admin_sessions`, `server_keys`, `player_identities`, `player_sessions`, `player_email_codes`, `player_data` |
| Naming | PascalCase (EF's default) | snake_case (configured explicitly) |
| Primary key | `uuid`, generated client-side | `bigint` identity, or a composite key for `player_identities` and `player_data` |
| Defined in | `server/Models/Players/` | `server/Models/Admin/`, `server/Models/Players/` + `OnModelCreating` in `server/Models/Db.cs` |

The admin and player-auth tables follow the project's documented conventions.
The game tables predate them and still use EF defaults. (`player_id` columns are
`uuid` because they point at `Players.Id`.)

!!! warning "New tables should follow the admin style"
    snake_case names, `bigint` identity keys. The game tables are the exception,
    not the pattern.

## Entities

### Game data

`server/Models/Players/` — one plain class per file, no base class, no annotations:

```csharp
public class Player
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? RegisteredHardwareId { get; set; }
}
```

`Player` holds no login details and no game values. How a player proves who they
are lives in `player_identities`, and values like XP live in `player_data` (both
below). There used to be an `Xp` column here, which the `MovePlayerXpToPlayerData`
migration moved. `DisplayName` is only what the game shows: it is
nullable, deliberately **not unique** (no index), and limited to 3–25 characters
by the app, not by the column. `RegisteredHardwareId` is the hardware ID the game
sent to `register`, kept for support and spotting reroll farms. It's never used to
find or sign in a player. It lives here rather than on the device identity because
that row is deleted when an email is verified.

- A `Character` belongs to a `Player` and carries no progression of its own yet
  — it is currently just identity.
- A `TelemetryEvent` is **player-scoped, not character-scoped**. `Data` holds
  the event payload as JSON *text*, and `Timestamp` is always set server-side in
  UTC.

!!! note "`Data` is `text`, not `jsonb`"
    So Postgres does not validate or index the payload. Querying inside it means
    casting. Fine while nothing queries it; a change to make when something does.

### Player auth data

`server/Models/Players/` (PlayerIdentity, PlayerSession, PlayerEmailCode). A player can have several **identities**; each
is one way to prove who they are.

| `provider` | Meaning | `subject` |
|---|---|---|
| `0` device | A guest's device token, minted by `register` | SHA-256 of the token, lowercase hex |
| `1` email | Email + password | the email's key: lowercased, and for Gmail with dots and `+tag` removed |
| `2` steam | Reserved, nothing creates it yet | — |

- **`(provider, subject)` is the primary key.** One device token or email can
  belong to at most one player.
- **Device subjects are hashes.** The token is a credential, so a database dump
  must not hand out working ones. Plain SHA-256 is enough because the token is
  256 random bits; there is nothing to brute-force. The `AddDeviceTokens`
  migration hashed the device IDs that existed before tokens did, so those keep
  working as tokens. Its `Down()` throws, because hashes can't be reversed.
- Postgres also enforces **at most one email identity per player** (the partial
  unique index `ux_player_identities_one_email`) and the provider range
  (`ck_player_identities_provider`).
- **Gmail aliases are one identity.** `subject` holds a key, not the typed
  address. Gmail ignores dots and anything after a `+`, and `googlemail.com` is the
  same inbox, so `Sher.Wyn+x@googlemail.com` and `sherwyn@gmail.com` both get the
  key `sherwyn@gmail.com` and can't be two accounts. Other providers keep dots and
  `+` as typed. The rule is `PlayerInputRules.EmailKey`. The
  `CanonicalizeGmailSubjects` migration rewrote existing Gmail rows to the key; if
  two existing rows would have folded to the same key, only the oldest was
  rewritten and the other keeps its old subject, so email login can no longer
  reach it. Its `Down()` puts `subject` back from `email`.
- **`email`** is the address as typed (lowercased) on email identities, and is where
  mail is sent. It is kept apart from `subject` because the two differ for Gmail,
  and because it is PII. **`pw_hash`** is the Argon2id encoded string, email only.
- Linking an email **adds** a row, and the device identity stays until the email
  is verified. **Verifying deletes the device identity** (same transaction), so a
  player with a verified email never has one.
- **`verified_at`** is null until the player enters the mailed code. Only a
  verified email can reset its password. Device identities never use it.
- **`player_email_codes`** holds the codes mailed for verification (`purpose` 0)
  and password reset (`1`). Only a hash of the code is stored, with a 15 minute
  expiry, an attempt counter and a `used_at`.
- **`player_sessions.token_hash`** is the SHA-256 of the session token, like the
  admin side. Sessions have one fixed expiry (30 days) and no idle timeout. Old
  rows are never cleaned up.
- Deleting a player cascades to its identities and sessions.

### Player data

`server/Models/Players/PlayerDataEntry.cs` and `PlayerDataTier.cs`. One row is one
named value for one player, like a PlayFab player data entry:

| Column | Type | Notes |
|---|---|---|
| `player_id` | `uuid` | Foreign key to `Players`, and the row **goes when the player goes** |
| `tier` | `smallint` | 0 `ClientWritable`, 1 `ReadOnly`, 2 `Internal`, enforced by `ck_player_data_tier` |
| `key` | `text` | 1-64 characters from `A-Z a-z 0-9 _ . -` (checked by the app) |
| `value` | `text` | Plain text, 16 KB at most (checked by the app). No `jsonb`, as in PlayFab |
| `updated_at` | `timestamptz` | Set by the server on every write |

- **The primary key is `(player_id, tier, key)`.** A player has one value per key
  in each tier, and the same key in two tiers is two separate entries.
- **The tier says who may change a value,** not who may read it. XP is a
  `ReadOnly` value: the client can show it but only a game server writes it. The
  numbers are stored, so they must never be renumbered.
- **Writes are upserts.** The server runs `INSERT ... ON CONFLICT (player_id, tier,
  key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at` for
  each key, inside one transaction, so a write sets values and never adds to them.
  The key and size limits (100 keys per tier, 10 changes per request) are checked
  by the app, with a lock per player and tier so two writes at once can't pass the
  100. See [API reference](../reference/api.md#player-data).
- **XP moved here in one migration,** `MovePlayerXpToPlayerData`, in this order so
  nobody loses any: create `player_data`, copy every non-zero `Players.Xp` into a
  `ReadOnly` key called `Xp` (its value is the number written as text), and only
  then drop the column. A player with 0 XP gets no row, since a missing key just
  means none yet. `Down()` adds the column back and restores each `Xp` it can: a
  value that isn't a whole number that fits in an integer comes back as 0, because
  the game server can write any text.

!!! warning "Run it on a copy of production before merging"
    Merging deploys, and the migration runs at startup. It is tested on made-up
    data (`PlayerDataMigrationTests`), but the real check is to restore a production
    backup into a scratch database, run `dotnet ef database update` against it, and
    confirm every non-zero XP became a `ReadOnly` `Xp` key.

### Admin data

`server/Models/Admin/`:

```csharp
public enum AdminRole : short
{
    Owner = 0,
    Admin = 1,
}
```

Two roles, and **Postgres enforces it** — not just the C# enum:

```csharp
e.ToTable("admin_users", t => t.HasCheckConstraint("ck_admin_users_role", "role IN (0, 1)"));
```

Adding a third role is therefore a migration, not just an enum edit. The
database will reject the insert otherwise.

Column notes:

- **`username`** is the login identifier, unique case-insensitively.
- **`email`** is optional, unused for login, and reserved for future report
  hooks. Do not add format validation: the seeded owner's value is not an email
  address.
- **`pw_hash`** is the full Argon2id *encoded string* — salt and parameters are
  inside it, so there is no separate salt column.
- **`totp_secret`** exists and is **read by nothing**. Dead for now.
- **`disabled_at`** is the soft-delete marker. Nothing is hard-deleted.
- **`token_hash`** on a session is the SHA-256 of the token. The raw token is
  never stored.
- **`ip`** is recorded at login and **never compared** against anything.

## Indexes and constraints

| Object | On | Why |
|---|---|---|
| `ux_admin_users_username_lower` | `lower(username)` | Case-insensitive unique login. Raw SQL, because EF cannot express a functional index. |
| Unique index | `admin_sessions.token_hash` | One session per token, and makes lookup an index hit |
| Unique index | `server_keys.key_sha256` | Looked up by hash on every server call, and no two keys can share one |
| Primary key | `player_data (player_id, tier, key)` | A player has one value per key in each tier, and the key is the way in |
| `ck_player_data_tier` | `tier IN (0, 1, 2)` | The tier enum, enforced in the database |
| FK, `ON DELETE CASCADE` | `player_data.player_id` | Deleting a player removes their data |
| `ck_admin_users_role` | `role IN (0, 1)` | The role enum, enforced in the database |
| FK, `ON DELETE CASCADE` | `admin_sessions.admin_id` | Deleting an admin row removes its sessions |
| FK, `NO ACTION` | `admin_users.created_by` | Self-reference; must not cascade admins into each other |

The login query is written so it uses that functional index:

```csharp
var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Username.ToLower() == username);
```

`.ToLower()` translates to `lower(username) = $1`, which matches the indexed
expression. Writing it any other way silently drops to a sequential scan.

!!! warning "The unique index covers disabled rows"
    A deactivated admin's username can **never** be reused — there is no partial
    `WHERE disabled_at IS NULL`. That is a deliberate consequence of soft
    delete, surfaced in the UI's confirmation prompt.

## Migrations

### They apply themselves

`Program.cs`, before the app serves anything:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<server.Models.Db>();
    db.Database.Migrate();
    await scope.ServiceProvider.GetRequiredService<OwnerSeeder>().Seed();
}
```

So nobody runs `dotnet ef database update` as part of normal work. Start the
app and the schema is current.

!!! note "This does not survive scaling out"
    Several app instances against one database would race to apply migrations on
    boot. Correct for one instance; a real problem the day a second appears.

### History

| Migration | What it did |
|---|---|
| `20260916031439_InitialCreate` | `Players` |
| `20260918051412_AddCharacterAndTelemetry` | `Characters`, `TelemetryEvents` |
| `20260925095216_AddAdminTables` | `admin_users`, `admin_sessions`, the role check, the lower-email index |
| `20260929061126_AddIdleExpiryToAdminSessions` | `idle_expires_at` |
| `20260930162910_AddUsernameToAdminUsers` | Added `username`, backfilled it from `email`, swapped the functional index |
| `20261001123215_AddUniqueIndexOnPlayerDeviceId` | A unique index on `Players.DeviceId`, the old guest credential (since replaced by `player_identities`) |
| `20261003052600_AddPlayerIdentitiesAndSessions` | `player_identities`, `player_sessions` and the one-email-per-player index |
| `20261003053816_AddEmailVerification` | `verified_at`, and `player_email_codes` |
| `20261003104040_AddPlayerDisplayName` | `Players.DisplayName` |
| `20261003111454_AddDeviceTokens` | Device logins move to server-made tokens stored as a SHA-256, and existing device rows are converted |
| `20261031133631_CanonicalizeGmailSubjects` | Existing Gmail email rows are rewritten to the folded key |
| `20261031140520_AddServerKeys` | `server_keys`: the hash of each game server key, with its expiry, revoked and last-used times |
| `20261102104420_MovePlayerXpToPlayerData` | `player_data`; copies every non-zero `Players.Xp` into a `ReadOnly` `Xp` key; drops `Players.Xp` |

`AddUsernameToAdminUsers` is worth reading as an example of a non-trivial
migration — it backfills existing rows before swapping the unique index, so the
seeded owner's account keeps working. `MovePlayerXpToPlayerData` is the other one
to read: it copies the data before it drops the column, and its order is what keeps
everyone's XP.

```csharp
migrationBuilder.Sql("UPDATE admin_users SET username = email WHERE username = '';");
migrationBuilder.Sql("DROP INDEX ux_admin_users_email_lower;");
migrationBuilder.Sql("CREATE UNIQUE INDEX ux_admin_users_username_lower ON admin_users (lower(username));");
```

### Adding one

```bash
dotnet tool install --global dotnet-ef   # once per machine
cd server
dotnet ef migrations add SomeChange
```

Then, before committing:

1. **Read the generated file.** EF guesses, and sometimes guesses wrong —
   especially around renames, which it may emit as drop-plus-add and lose data.
2. **Check the `Down` method** actually reverses `Up`.
3. **Commit the `.Designer.cs` and the updated `DbModelSnapshot.cs` too.**
   Leaving the snapshot behind makes the next person's migration diff against
   stale state.
4. **Add data-moving SQL by hand** if a column needs backfilling. EF will not
   write that for you.

!!! danger "Never edit an applied migration"
    Once a migration has run anywhere — including a teammate's machine — it is
    history. Fix it forward with a new migration.

### The `idle_expires_at` default

That migration added the column as `NOT NULL DEFAULT '0001-01-01'`. The
year-one value correctly invalidated every pre-existing session, which was the
point. But **the default is still on the column**, so any future insert path
that forgets the field silently creates a session that is already dead. Worth
dropping when someone is next in there.

## Connection strings: two readers

A persistent source of confusion. There are two mechanisms and they do not see
each other:

| Who is running | Reads | Value |
|---|---|---|
| `dotnet watch` on your machine | `server/appsettings.Development.json` | `Host=localhost;Port=5432;…` |
| Anything in Docker | `ConnectionStrings__Db` env var from the compose file | `Host=postgres;…` |

The env var wins when both are present, because environment variables override
`appsettings.*.json` in ASP.NET's configuration order. The hostname is the
tell: `localhost` means the host process, `postgres` means inside the Docker
network.

`.env` is read by **Compose only**. `dotnet watch` never sees it.

## Backups

```bash
# back up (dev)
docker compose -f compose.dev.yaml exec -T postgres \
  pg_dump -U hakutaku hakutaku | gzip > backup-$(date +%F).sql.gz

# restore
gunzip -c backup-2026-09-16.sql.gz | \
  docker compose -f compose.dev.yaml exec -T postgres psql -U hakutaku hakutaku
```

For production, swap in `-p hakutaku -f compose.yaml`.

!!! warning "There is no scheduled backup"
    Nothing runs these automatically. Take one by hand before anything
    schema-shaped in production.

## Known issues

- **Player sessions pile up.** Every register or email login adds a
  `player_sessions` row and nothing deletes expired ones. Same story as
  `admin_sessions`, and as `player_email_codes`.
- **`TelemetryEvent.Data` is `text`**, so payloads are neither validated nor
  indexable without casting.
- **`player_data` values are untyped text.** The game server decides what is in
  them, so the database can't total or sort XP without casting it.
- **Npgsql's default pool is 100 connections** and Postgres' default
  `max_connections` is also 100. One app instance is fine; two could exhaust it.
- **IDs will change.** Game entities use GUIDs; planning documents target
  `bigint`. Treat IDs as opaque strings everywhere outside the server.
