// Player data as the admin UI sees it: the types the server answers with, and the
// same checks the server makes (server/Services/PlayerDataRules.cs, Limits.cs), so
// a mistake shows next to the field instead of as a 400 after Save. The server
// still checks everything; these only keep the page honest.

// A row of GET /api/players. createdAt is read from the id (null if it isn't a UUIDv7),
// lastLoginAt is the newest session (null if they never signed in).
export type Player = {
  id: string
  displayName: string | null
  providers: string[]
  createdAt: string | null
  lastLoginAt: string | null
}

// GET /api/players/{id}: a row plus each linked account. No email addresses, by design.
export type Account = { provider: string; linkedAt: string; verified: boolean }
export type PlayerDetail = Player & { accounts: Account[] }

export const when = (iso: string | null, fallback = 'Never') => (iso ? new Date(iso).toLocaleString() : fallback)

// "3 hours ago", for the at-a-glance columns; the exact time sits in the title.
export function ago(iso: string | null, fallback = 'Never'): string {
  if (!iso) return fallback
  const s = (Date.now() - new Date(iso).getTime()) / 1000
  if (s < 60) return 'just now'
  const steps: [number, string][] = [[60, 'minute'], [3600, 'hour'], [86400, 'day'], [2592000, 'month'], [31536000, 'year']]
  let unit = steps[0]
  for (const step of steps) if (s >= step[0]) unit = step
  const n = Math.floor(s / unit[0])
  return `${n} ${unit[1]}${n === 1 ? '' : 's'} ago`
}

export type TierName = 'ClientWritable' | 'ReadOnly' | 'Internal'
export type Entry = { key: string; value: string; updatedAt: string }
export type TierData = { tier: TierName; entries: Entry[] }

export const TIERS: { name: TierName; label: string; blurb: string }[] = [
  { name: 'ClientWritable', label: 'Client writable', blurb: "The player's game client may change these." },
  { name: 'ReadOnly', label: 'Read only', blurb: 'The client can read these, but only a game server writes them. XP lives here.' },
  { name: 'Internal', label: 'Internal', blurb: 'Never shown to the client. Only the backend and game servers use these.' },
]

export const LIMITS = {
  keyMaxLength: 64,
  valueMaxBytes: 16 * 1024,
  keysPerTier: 100,
  changesPerSave: 10,
}

// A plain $ in JS matches only at the very end (no multiline flag), so "Xp\n" fails here
// just as it does on the server.
const KEY_PATTERN = /^[A-Za-z0-9_.-]+$/

export function keyProblem(key: string): string | null {
  if (key.length === 0) return 'A key is required.'
  if (key.length > LIMITS.keyMaxLength) return `Keys are at most ${LIMITS.keyMaxLength} characters.`
  if (!KEY_PATTERN.test(key)) return 'Keys use only letters, digits, _ . and -'
  return null
}

const encoder = new TextEncoder()
export const byteLength = (value: string) => encoder.encode(value).length

export function valueProblem(value: string): string | null {
  if (value.includes('\0')) return 'Values cannot hold a null character.'
  if (byteLength(value) > LIMITS.valueMaxBytes) return `Values are at most ${LIMITS.valueMaxBytes / 1024} KB.`
  return null
}

export function formatBytes(n: number): string {
  return n < 1024 ? `${n} B` : `${(n / 1024).toFixed(1)} KB`
}

// Only objects and arrays count: "12" and "true" parse as JSON too, but there is
// nothing to pretty-print in them.
export function parseJson(value: string): unknown | undefined {
  const t = value.trim()
  if (!(t.startsWith('{') || t.startsWith('['))) return undefined
  try {
    return JSON.parse(t)
  } catch {
    return undefined
  }
}

// Short, single-line values are edited right in the table, like PlayFab. Anything
// longer, multi-line or JSON opens the bigger editor instead.
export const editsInline = (value: string) => value.length <= 120 && !value.includes('\n') && parseJson(value) === undefined

export const playerName = (p: Pick<Player, 'displayName'>) => p.displayName ?? 'Unnamed player'
