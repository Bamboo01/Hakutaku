<!-- One player's page: who they are, then their player data in the three tiers,
     laid out like PlayFab's Player Data tab. Short values are edited right in the
     table, longer ones or JSON in ValueDialog. Edits, new keys and removals are
     held on the page, per tier, until Save, which sends that tier's changes as one
     POST /api/players/{id}/data. The server holds it to the same rules as a game
     server's write and logs who made it. -->
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { RouterLink, onBeforeRouteLeave } from 'vue-router'
import AppAlert from '../components/AppAlert.vue'
import ValueDialog from '../components/ValueDialog.vue'
import { ApiError, api } from '../api'
import {
  type Entry,
  LIMITS,
  type Player,
  TIERS,
  type TierData,
  type TierName,
  editsInline,
  formatBytes,
  byteLength,
  playerName,
  valueProblem,
} from '../playerData'

const props = defineProps<{ id: string }>()

const player = ref<Player | null>(null)
const saved = reactive<Record<TierName, Entry[]>>({ ClientWritable: [], ReadOnly: [], Internal: [] })
const loading = ref(true)
const missing = ref(false)
const error = ref('')
const notice = ref('')
const saving = ref(false)
const copyNote = ref('')

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

// Why Save can't go yet, or null. Same limits the server enforces.
const blocker = computed(() => {
  const tier = active.value
  if (rows.value.some((r) => r.problem)) return 'Fix the values marked in red first.'
  if (changes(tier) > LIMITS.changesPerSave)
    return `At most ${LIMITS.changesPerSave} changes can be saved at once. Discard some, or save in smaller steps.`
  if (keyCount(tier) > LIMITS.keysPerTier) return `A player can have at most ${LIMITS.keysPerTier} keys in a tier.`
  return null
})

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
  if (row.state !== 'new') d.removed.push(row.key)
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
    player.value = await api.get<Player>(`/api/players/${props.id}`)
    await loadData()
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) missing.value = true
    else error.value = e instanceof ApiError ? e.message : 'Could not load this player.'
  } finally {
    loading.value = false
  }
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

const when = (iso: string) => new Date(iso).toLocaleString()
const preview = (v: string) => (v.length > 160 ? v.slice(0, 160) + '…' : v)
</script>

<template>
  <p class="crumbs"><RouterLink to="/players">Players</RouterLink> <span class="dim">/</span> {{ player ? playerName(player) : '…' }}</p>

  <p v-if="loading" class="dim">Loading…</p>
  <AppAlert v-else-if="missing" kind="info" message="There is no player with this id." />

  <template v-else-if="player">
    <section class="card">
      <div>
        <h1 :class="{ dim: !player.displayName }">{{ playerName(player) }}</h1>
        <p class="id">
          <code>{{ player.id }}</code>
          <button class="secondary small" @click="copyId">{{ copyNote || 'Copy id' }}</button>
        </p>
      </div>
      <div class="providers">
        <span class="dim label">Sign-in</span>
        <span v-for="pr in player.providers" :key="pr" class="chip">{{ pr }}</span>
        <span v-if="!player.providers.length" class="dim">none</span>
      </div>
    </section>

    <h2>Player data</h2>

    <div class="tabs" role="tablist">
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

    <p class="blurb dim">{{ activeTier.blurb }} <span class="limit">{{ keyCount(active) }} / {{ LIMITS.keysPerTier }} keys</span></p>

    <AppAlert v-if="error" :message="error" />
    <AppAlert v-if="notice" kind="info" :message="notice" />

    <table class="kv">
      <thead>
        <tr>
          <th class="k">Key</th>
          <th>Value</th>
          <th class="t">Last updated</th>
          <th class="a"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in rows" :key="r.key" :class="r.state">
          <td class="k">
            <code>{{ r.key }}</code>
            <span v-if="r.state === 'new'" class="tag">new</span>
            <span v-else-if="r.state === 'edited'" class="tag">edited</span>
            <span v-else-if="r.state === 'removed'" class="tag del">removed</span>
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
          <td class="t dim">{{ r.updatedAt ? when(r.updatedAt) : '—' }}</td>
          <td class="a">
            <template v-if="r.state === 'removed'">
              <button class="secondary small" @click="undo(active, r.key)">Undo</button>
            </template>
            <template v-else>
              <button class="secondary small" :aria-label="`Open ${r.key} in the editor`" title="Open in the editor" @click="openEdit(r)">⤢</button>
              <button v-if="r.state === 'edited'" class="secondary small" title="Undo this edit" @click="undo(active, r.key)">↺</button>
              <button class="danger small" :aria-label="`Remove ${r.key}`" title="Remove" @click="remove(active, r)">✕</button>
            </template>
          </td>
        </tr>
        <tr v-if="!rows.length">
          <td colspan="4" class="dim empty">No {{ activeTier.label.toLowerCase() }} data for this player yet.</td>
        </tr>
      </tbody>
    </table>

    <button class="secondary add" @click="openAdd">+ Add key</button>

    <div v-if="changes(active)" class="savebar">
      <span>
        <strong>{{ changes(active) }}</strong> unsaved change{{ changes(active) === 1 ? '' : 's' }} in {{ activeTier.label.toLowerCase() }}
        <span v-if="blocker" class="err"> · {{ blocker }}</span>
      </span>
      <span class="spacer" />
      <button class="secondary" :disabled="saving" @click="discard(active)">Discard</button>
      <button :disabled="saving || !!blocker" @click="save">{{ saving ? 'Saving…' : 'Save changes' }}</button>
    </div>

    <ValueDialog :open="dialog" :taken="takenKeys" @apply="applyDialog" @close="dialog = null" />
  </template>

  <AppAlert v-else-if="error" :message="error" />
</template>

<style scoped>
h1 { margin: 0 0 0.35rem; font-size: 1.3rem; }
h2 { margin: 1.75rem 0 0.75rem; font-size: 1.05rem; }
.dim { color: var(--text-dim); }
.mono { font-family: var(--mono); font-size: 0.82rem; }
.small { padding: 0.2rem 0.55rem; font-size: 0.8rem; }
.spacer { flex: 1; }
.err { color: var(--danger); font-size: 0.8rem; }

.crumbs { margin: 0 0 1rem; font-size: 0.9rem; }
.crumbs a { color: var(--accent); }

.card {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.1rem;
  border: 1px solid var(--border);
  border-top: 4px solid var(--matcha);
  border-radius: var(--radius);
  background: var(--surface);
}
.id { display: flex; align-items: center; gap: 0.5rem; margin: 0; flex-wrap: wrap; }
.id code { font-size: 0.82rem; color: var(--text-dim); word-break: break-all; }
.providers { display: flex; align-items: center; gap: 0.35rem; }
.label { font-size: 0.8rem; margin-right: 0.2rem; }
.chip {
  padding: 0.1rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.78rem;
  background: var(--bg);
}

.tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--border); overflow-x: auto; }
.tabs button {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.55rem 0.9rem;
  border: none;
  border-bottom: 3px solid transparent;
  border-radius: 0;
  background: transparent;
  color: var(--text-dim);
  white-space: nowrap;
}
.tabs button.on { color: var(--text); border-bottom-color: var(--accent); font-weight: 600; }
.count { font-size: 0.75rem; padding: 0 0.4rem; border-radius: 999px; background: var(--surface); border: 1px solid var(--border); }
.dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); }

.blurb { margin: 0.75rem 0 1rem; font-size: 0.85rem; display: flex; gap: 1rem; flex-wrap: wrap; }
.limit { margin-left: auto; font-variant-numeric: tabular-nums; }

.kv { table-layout: fixed; }
.kv th.k, .kv td.k { width: 26%; }
.kv th.t, .kv td.t { width: 12rem; font-size: 0.8rem; }
.kv th.a, .kv td.a { width: 7.5rem; text-align: right; white-space: nowrap; }
.kv td { vertical-align: top; }
.kv td.k code { font-size: 0.85rem; word-break: break-all; }
.kv td.v input { padding: 0.35rem 0.5rem; }
.kv td.a button + button { margin-left: 0.25rem; }
.kv td.empty { padding: 1.25rem 0.75rem; }

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

.add { margin-top: 0.75rem; }

.savebar {
  position: sticky;
  bottom: 0;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-top: 1.25rem;
  padding: 0.75rem 1rem;
  border: 1px solid var(--accent);
  border-radius: var(--radius);
  background: var(--surface);
  font-size: 0.9rem;
}

@media (max-width: 720px) {
  .kv th.t, .kv td.t { display: none; }
  .kv th.k, .kv td.k { width: 34%; }
}
</style>
