<!-- One player's page, laid out like PlayFab's player view: the player's name over
     two tabs, Overview (who they are, when they joined and last signed in, their
     linked accounts, a count of their data) and Player Data.

     Player Data is a card with a tier switch (Client writable / Read only /
     Internal) and a toolbar over a Key / Value / Last updated table with a checkbox
     on each row. Short values are edited right in the table, longer ones or JSON in
     ValueDialog. Edits, new keys and removals are held on the page, per tier, until
     Save, which sends that tier's changes as one POST /api/players/{id}/data. The
     server holds it to the same rules as a game server's write and logs who made it.
     The tab is in the URL (?tab=data), so it can be linked to. -->
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { RouterLink, onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import AppAlert from '../components/AppAlert.vue'
import AppCard from '../components/AppCard.vue'
import ValueDialog from '../components/ValueDialog.vue'
import { ApiError, api } from '../api'
import {
  type Entry,
  LIMITS,
  type PlayerDetail,
  TIERS,
  type TierData,
  type TierName,
  ago,
  byteLength,
  editsInline,
  formatBytes,
  playerName,
  valueProblem,
  when,
} from '../playerData'

const props = defineProps<{ id: string }>()
const route = useRoute()
const router = useRouter()

const player = ref<PlayerDetail | null>(null)
const saved = reactive<Record<TierName, Entry[]>>({ ClientWritable: [], ReadOnly: [], Internal: [] })
const loading = ref(true)
const missing = ref(false)
const error = ref('')
const notice = ref('')
const saving = ref(false)
const copyNote = ref('')

// ---- page tabs, kept in the URL

type PageTab = 'overview' | 'data'
const tab = computed<PageTab>(() => (route.query.tab === 'data' ? 'data' : 'overview'))
const showTab = (t: PageTab) => router.replace({ query: { ...route.query, tab: t === 'overview' ? undefined : t } })

// ---- tiers

const active = ref<TierName>('ReadOnly')
const activeTier = computed(() => TIERS.find((t) => t.name === active.value)!)

// Unsaved work, per tier. `set` holds only values that differ from what is saved
// (or keys that are new), so an edit typed back to the original is no change at all.
type Draft = { set: Record<string, string>; removed: string[] }
const drafts = reactive<Record<TierName, Draft>>({
  ClientWritable: { set: {}, removed: [] },
  ReadOnly: { set: {}, removed: [] },
  Internal: { set: {}, removed: [] },
})

type RowState = 'same' | 'edited' | 'new' | 'removed'
type Row = { key: string; value: string; updatedAt: string | null; state: RowState; problem: string | null }

function rowsOf(tier: TierName): Row[] {
  const d = drafts[tier]
  const rows: Row[] = saved[tier].map((e) => {
    const removed = d.removed.includes(e.key)
    const value = d.set[e.key] ?? e.value
    const state: RowState = removed ? 'removed' : e.key in d.set ? 'edited' : 'same'
    return { key: e.key, value, updatedAt: e.updatedAt, state, problem: state === 'edited' ? valueProblem(value) : null }
  })
  for (const [key, value] of Object.entries(d.set)) {
    if (!saved[tier].some((e) => e.key === key))
      rows.push({ key, value, updatedAt: null, state: 'new', problem: valueProblem(value) })
  }
  return rows
}

const rows = computed(() => rowsOf(active.value))
const changes = (tier: TierName) => Object.keys(drafts[tier].set).length + drafts[tier].removed.length
const totalChanges = computed(() => TIERS.reduce((n, t) => n + changes(t.name), 0))
const keyCount = (tier: TierName) => rowsOf(tier).filter((r) => r.state !== 'removed').length
const savedCount = (tier: TierName) => saved[tier].length

// Why Save can't go yet, or null. Same limits the server enforces.
const blocker = computed(() => {
  const tier = active.value
  if (rows.value.some((r) => r.problem)) return 'Fix the values marked in red first.'
  if (changes(tier) > LIMITS.changesPerSave)
    return `At most ${LIMITS.changesPerSave} changes can be saved at once. Discard some, or save in smaller steps.`
  if (keyCount(tier) > LIMITS.keysPerTier) return `A player can have at most ${LIMITS.keysPerTier} keys in a tier.`
  return null
})

// ---- selecting rows, for Delete selected

const selected = ref<string[]>([])
watch(active, () => (selected.value = []))
const selectable = computed(() => rows.value.filter((r) => r.state !== 'removed').map((r) => r.key))
const allSelected = computed(() => selectable.value.length > 0 && selectable.value.every((k) => selected.value.includes(k)))
const toggleAll = () => (selected.value = allSelected.value ? [] : [...selectable.value])
function toggleRow(key: string) {
  selected.value = selected.value.includes(key) ? selected.value.filter((k) => k !== key) : [...selected.value, key]
}
function removeSelected() {
  for (const r of rows.value) if (selected.value.includes(r.key)) remove(active.value, r)
  selected.value = []
}

// ---- editing

function setValue(tier: TierName, key: string, value: string) {
  const d = drafts[tier]
  d.removed = d.removed.filter((k) => k !== key)
  const original = saved[tier].find((e) => e.key === key)
  if (original && original.value === value) delete d.set[key]
  else d.set[key] = value
  notice.value = ''
}

function remove(tier: TierName, row: Row) {
  const d = drafts[tier]
  delete d.set[row.key]
  if (row.state !== 'new' && !d.removed.includes(row.key)) d.removed.push(row.key)
  selected.value = selected.value.filter((k) => k !== row.key)
  notice.value = ''
}

function undo(tier: TierName, key: string) {
  const d = drafts[tier]
  delete d.set[key]
  d.removed = d.removed.filter((k) => k !== key)
}

function discard(tier: TierName) {
  drafts[tier].set = {}
  drafts[tier].removed = []
  notice.value = ''
}

// ---- the bigger editor

const dialog = ref<{ key: string; value: string; adding: boolean } | null>(null)
const takenKeys = computed(() => rows.value.filter((r) => r.state !== 'removed').map((r) => r.key))

const openAdd = () => (dialog.value = { key: '', value: '', adding: true })
const openEdit = (row: Row) => (dialog.value = { key: row.key, value: row.value, adding: false })

function applyDialog(key: string, value: string) {
  setValue(active.value, key, value)
  dialog.value = null
}

// ---- server

async function loadData() {
  const data = await api.get<{ tiers: TierData[] }>(`/api/players/${props.id}/data`)
  for (const t of data.tiers) saved[t.tier] = t.entries
}

async function load() {
  loading.value = true
  error.value = ''
  try {
    player.value = await api.get<PlayerDetail>(`/api/players/${props.id}`)
    await loadData()
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) missing.value = true
    else error.value = e instanceof ApiError ? e.message : 'Could not load this player.'
  } finally {
    loading.value = false
  }
}

async function refresh() {
  if (totalChanges.value && !confirm('Reload from the server? Your unsaved changes will be lost.')) return
  for (const t of TIERS) discard(t.name)
  await load()
}

async function save() {
  const tier = active.value
  const n = changes(tier)
  if (!n || blocker.value || !player.value) return
  const ok = confirm(
    `Save ${n} change${n === 1 ? '' : 's'} to the ${activeTier.value.label.toLowerCase()} data of ${playerName(player.value)}?\n\n` +
      'This changes a live player straight away, and a game server reading this data will see it.',
  )
  if (!ok) return

  error.value = ''
  saving.value = true
  try {
    const d = drafts[tier]
    await api.post(`/api/players/${props.id}/data?tier=${tier}`, { data: d.set, keysToRemove: d.removed })
    discard(tier)
    selected.value = []
    await loadData()
    notice.value = `Saved ${n} change${n === 1 ? '' : 's'}.`
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not save the changes.'
  } finally {
    saving.value = false
  }
}

async function copyId() {
  try {
    await navigator.clipboard.writeText(props.id)
    copyNote.value = 'Copied'
  } catch {
    copyNote.value = 'Could not copy'
  }
  setTimeout(() => (copyNote.value = ''), 1500)
}

// ---- don't lose unsaved work

onBeforeRouteLeave(() => !totalChanges.value || confirm('Leave this player? Your unsaved changes will be lost.'))

function warnUnload(e: BeforeUnloadEvent) {
  if (totalChanges.value) e.preventDefault()
}
onMounted(() => {
  window.addEventListener('beforeunload', warnUnload)
  load()
})
onBeforeUnmount(() => window.removeEventListener('beforeunload', warnUnload))

const preview = (v: string) => (v.length > 160 ? v.slice(0, 160) + '…' : v)
function openTier(t: TierName) {
  active.value = t
  showTab('data')
}
</script>

<template>
  <p class="crumbs"><RouterLink to="/players">Players</RouterLink> <span class="dim">/</span> {{ player ? playerName(player) : '…' }}</p>

  <p v-if="loading && !player" class="dim">Loading…</p>
  <AppAlert v-else-if="missing" kind="info" message="There is no player with this id." />

  <template v-else-if="player">
    <div class="title">
      <h1 :class="{ dim: !player.displayName }">{{ playerName(player) }}</h1>
      <span class="spacer" />
      <button class="secondary small" :disabled="loading" @click="refresh">Refresh</button>
    </div>

    <div class="tabs" role="tablist">
      <button role="tab" :aria-selected="tab === 'overview'" :class="{ on: tab === 'overview' }" @click="showTab('overview')">Overview</button>
      <button role="tab" :aria-selected="tab === 'data'" :class="{ on: tab === 'data' }" @click="showTab('data')">
        Player Data
        <span v-if="totalChanges" class="dot" :title="`${totalChanges} unsaved`" />
      </button>
    </div>

    <AppAlert v-if="error" :message="error" />

    <!-- ---------------- Overview ---------------- -->
    <div v-if="tab === 'overview'" class="grid">
      <AppCard title="Player info">
        <dl>
          <dt>Display name</dt>
          <dd :class="{ dim: !player.displayName }">{{ player.displayName ?? 'Not set' }}</dd>
          <dt>Player ID</dt>
          <dd class="id">
            <code>{{ player.id }}</code>
            <button class="secondary small" @click="copyId">{{ copyNote || 'Copy' }}</button>
          </dd>
          <dt>Created</dt>
          <dd>{{ when(player.createdAt, 'Unknown') }} <span class="dim">({{ ago(player.createdAt, 'unknown') }})</span></dd>
          <dt>Last login</dt>
          <dd>{{ when(player.lastLoginAt) }} <span v-if="player.lastLoginAt" class="dim">({{ ago(player.lastLoginAt) }})</span></dd>
        </dl>
      </AppCard>

      <AppCard title="Linked accounts" flush>
        <table>
          <thead>
            <tr><th>Provider</th><th>Linked</th><th>Status</th></tr>
          </thead>
          <tbody>
            <tr v-for="a in player.accounts" :key="a.provider">
              <td><span class="chip">{{ a.provider }}</span></td>
              <td class="dim" :title="when(a.linkedAt)">{{ ago(a.linkedAt) }}</td>
              <td>
                <span v-if="a.provider !== 'email'" class="dim">—</span>
                <span v-else-if="a.verified" class="ok">Verified</span>
                <span v-else class="warn">Unverified</span>
              </td>
            </tr>
            <tr v-if="!player.accounts.length">
              <td colspan="3" class="dim">No linked accounts. This player can't sign in.</td>
            </tr>
          </tbody>
        </table>
      </AppCard>

      <AppCard title="Player data" flush>
        <table>
          <thead>
            <tr><th>Tier</th><th>Keys</th></tr>
          </thead>
          <tbody>
            <tr v-for="t in TIERS" :key="t.name" class="link" tabindex="0" @click="openTier(t.name)" @keydown.enter="openTier(t.name)">
              <td>{{ t.label }}</td>
              <td>{{ savedCount(t.name) }} <span class="dim">/ {{ LIMITS.keysPerTier }}</span></td>
            </tr>
          </tbody>
        </table>
      </AppCard>
    </div>

    <!-- ---------------- Player Data ---------------- -->
    <AppCard v-else flush>
      <template #head>
        <div class="seg" role="tablist" aria-label="Tier">
          <button
            v-for="t in TIERS"
            :key="t.name"
            role="tab"
            :aria-selected="active === t.name"
            :class="{ on: active === t.name }"
            @click="active = t.name"
          >
            {{ t.label }}
            <span class="count">{{ keyCount(t.name) }}</span>
            <span v-if="changes(t.name)" class="dot" :title="`${changes(t.name)} unsaved`" />
          </button>
        </div>
        <span class="spacer" />
        <span class="dim limit">{{ keyCount(active) }} / {{ LIMITS.keysPerTier }} keys</span>
      </template>

      <p class="blurb dim">{{ activeTier.blurb }}</p>

      <div class="toolbar">
        <button @click="openAdd">+ Add key</button>
        <button class="danger" :disabled="!selected.length" @click="removeSelected">
          Delete{{ selected.length ? ` (${selected.length})` : '' }}
        </button>
        <span class="spacer" />
        <span v-if="changes(active)" class="pending">
          <strong>{{ changes(active) }}</strong> unsaved change{{ changes(active) === 1 ? '' : 's' }}
        </span>
        <button class="secondary" :disabled="saving || !changes(active)" @click="discard(active)">Discard</button>
        <button :disabled="saving || !changes(active) || !!blocker" @click="save">{{ saving ? 'Saving…' : 'Save' }}</button>
      </div>
      <p v-if="changes(active) && blocker" class="blocker err">{{ blocker }}</p>
      <AppAlert v-if="notice" kind="info" :message="notice" class="notice" />

      <table class="kv">
        <thead>
          <tr>
            <th class="c">
              <input type="checkbox" :checked="allSelected" :disabled="!selectable.length" aria-label="Select all keys" @change="toggleAll" />
            </th>
            <th class="k">Key</th>
            <th>Value</th>
            <th class="t">Last updated</th>
            <th class="a"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rows" :key="r.key" :class="[r.state, { picked: selected.includes(r.key) }]">
            <td class="c">
              <input
                type="checkbox"
                :checked="selected.includes(r.key)"
                :disabled="r.state === 'removed'"
                :aria-label="`Select ${r.key}`"
                @change="toggleRow(r.key)"
              />
            </td>
            <td class="k">
              <code>{{ r.key }}</code>
              <span v-if="r.state === 'new'" class="tag">new</span>
              <span v-else-if="r.state === 'edited'" class="tag">edited</span>
              <span v-else-if="r.state === 'removed'" class="tag del">deleted</span>
            </td>
            <td class="v">
              <span v-if="r.state === 'removed'" class="mono struck">{{ preview(r.value) }}</span>
              <input
                v-else-if="editsInline(r.value)"
                class="mono"
                :value="r.value"
                :aria-label="`Value of ${r.key}`"
                spellcheck="false"
                @input="setValue(active, r.key, ($event.target as HTMLInputElement).value)"
              />
              <button v-else class="value-preview mono" :title="`${formatBytes(byteLength(r.value))} — click to edit`" @click="openEdit(r)">
                {{ preview(r.value) }}
              </button>
              <small v-if="r.problem" class="err">{{ r.problem }}</small>
            </td>
            <td class="t dim" :title="r.updatedAt ? when(r.updatedAt) : ''">{{ r.updatedAt ? ago(r.updatedAt) : '—' }}</td>
            <td class="a">
              <button v-if="r.state === 'removed' || r.state === 'edited'" class="secondary small" @click="undo(active, r.key)">Undo</button>
              <button v-if="r.state !== 'removed'" class="secondary small" :aria-label="`Open ${r.key} in the editor`" title="Open in the editor" @click="openEdit(r)">⤢</button>
            </td>
          </tr>
          <tr v-if="!rows.length">
            <td colspan="5" class="dim empty">No {{ activeTier.label.toLowerCase() }} data for this player yet. Use “+ Add key”.</td>
          </tr>
        </tbody>
      </table>
    </AppCard>

    <ValueDialog :open="dialog" :taken="takenKeys" @apply="applyDialog" @close="dialog = null" />
  </template>

  <AppAlert v-else-if="error" :message="error" />
</template>

<style scoped>
h1 { margin: 0; font-size: 1.5rem; font-weight: 600; }
.dim { color: var(--text-dim); }
.mono { font-family: var(--mono); font-size: 0.82rem; }
.small { padding: 0.2rem 0.55rem; font-size: 0.8rem; }
.spacer { flex: 1; }
.err { color: var(--danger); font-size: 0.8rem; }

.crumbs { margin: 0 0 0.5rem; font-size: 0.85rem; }
.crumbs a { color: var(--accent); }
.title { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1rem; }

/* page tabs, PlayFab-style underline */
.tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--border); margin-bottom: 1.25rem; }
.tabs button {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.6rem 1rem;
  border: none;
  border-bottom: 3px solid transparent;
  border-radius: 0;
  background: transparent;
  color: var(--text-dim);
  font-weight: 500;
}
.tabs button.on { color: var(--text); border-bottom-color: var(--accent); font-weight: 600; }
.dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); }

/* overview */
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 1.25rem; align-items: start; }
dl { display: grid; grid-template-columns: max-content 1fr; gap: 0.6rem 1.25rem; margin: 0; font-size: 0.9rem; }
dt { color: var(--text-dim); font-weight: 600; }
dd { margin: 0; min-width: 0; }
.id { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
.id code { font-size: 0.8rem; word-break: break-all; }
.chip {
  padding: 0.1rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.78rem;
  background: var(--surface);
}
.ok { color: var(--accent); font-weight: 600; font-size: 0.85rem; }
.warn { color: var(--danger); font-size: 0.85rem; }
.link { cursor: pointer; }
.link:hover td, .link:focus-visible td { background: var(--surface); }

/* player data card */
.seg { display: flex; gap: 0.25rem; flex-wrap: wrap; }
.seg button {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.35rem 0.75rem;
  border: 1px solid var(--border);
  background: var(--bg);
  color: var(--text-dim);
  font-size: 0.88rem;
}
.seg button.on { background: var(--matcha); color: #14170f; border-color: var(--matcha); font-weight: 600; }
.count { font-size: 0.72rem; padding: 0 0.4rem; border-radius: 999px; background: var(--surface); border: 1px solid var(--border); color: var(--text-dim); }
.limit { font-size: 0.85rem; font-variant-numeric: tabular-nums; }

.blurb { margin: 0; padding: 0.75rem 1rem 0; font-size: 0.85rem; }
.toolbar { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; padding: 0.75rem 1rem; border-bottom: 1px solid var(--border); }
.pending { font-size: 0.85rem; margin-right: 0.25rem; }
.blocker { margin: 0; padding: 0.5rem 1rem; border-bottom: 1px solid var(--border); }
.notice { margin: 0.75rem 1rem 0; }

.kv { table-layout: fixed; }
.kv th.c, .kv td.c { width: 2.75rem; }
.kv th.k, .kv td.k { width: 24%; }
.kv th.t, .kv td.t { width: 9rem; font-size: 0.8rem; }
.kv th.a, .kv td.a { width: 6.5rem; text-align: right; white-space: nowrap; }
.kv td { vertical-align: top; }
.kv td.c input { width: auto; margin-top: 0.45rem; }
.kv th.c input { width: auto; }
.kv td.k code { font-size: 0.85rem; word-break: break-all; line-height: 2; }
.kv td.v input { padding: 0.35rem 0.5rem; }
.kv td.t { padding-top: 0.95rem; }
.kv td.a button + button { margin-left: 0.25rem; }
.kv td.empty { padding: 1.25rem 1rem; }
tr.picked td { background: var(--surface); }

.tag { margin-left: 0.4rem; font-size: 0.7rem; padding: 0 0.35rem; border-radius: 4px; background: var(--matcha); color: #14170f; }
.tag.del { background: transparent; color: var(--danger); border: 1px solid var(--border); }
tr.new td:first-child, tr.edited td:first-child { box-shadow: inset 3px 0 0 var(--accent); }
tr.removed td:first-child { box-shadow: inset 3px 0 0 var(--danger); }
tr.removed td:not(.a) { opacity: 0.6; }
.struck { text-decoration: line-through; white-space: pre-wrap; word-break: break-all; }

.value-preview {
  display: block;
  width: 100%;
  text-align: left;
  padding: 0.35rem 0.5rem;
  border: 1px dashed var(--border);
  background: var(--surface);
  color: var(--text);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 6.5em;
  overflow: hidden;
}

@media (max-width: 760px) {
  .kv th.t, .kv td.t { display: none; }
  .kv th.k, .kv td.k { width: 32%; }
}
</style>
