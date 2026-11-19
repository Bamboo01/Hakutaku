<!-- Players page, like PlayFab's Identity → Players.
     Lists every player (the server answers with the whole table, no paging) in a
     card with a search over name and id, newest first, and sortable by name,
     created or last login. A row opens that player's page. Any signed-in admin. -->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppAlert from '../components/AppAlert.vue'
import AppCard from '../components/AppCard.vue'
import { ApiError, api } from '../api'
import { type Player, ago, playerName, when } from '../playerData'

const router = useRouter()

const players = ref<Player[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

type SortKey = 'name' | 'createdAt' | 'lastLoginAt'
const sortKey = ref<SortKey>('createdAt')
const sortDesc = ref(true)

function sortBy(k: SortKey) {
  if (sortKey.value === k) sortDesc.value = !sortDesc.value
  else {
    sortKey.value = k
    // names read best A→Z; dates newest first
    sortDesc.value = k !== 'name'
  }
}

// Missing values (never signed in, no name) always sink to the bottom, whichever way it's sorted.
function compare(a: Player, b: Player): number {
  const pick = (p: Player) => (sortKey.value === 'name' ? p.displayName?.toLowerCase() ?? null : p[sortKey.value])
  const x = pick(a)
  const y = pick(b)
  if (x === y) return 0
  if (x === null) return 1
  if (y === null) return -1
  return (x < y ? -1 : 1) * (sortDesc.value ? -1 : 1)
}

const shown = computed(() => {
  const q = search.value.trim().toLowerCase()
  const list = q ? players.value.filter((p) => p.id.includes(q) || (p.displayName ?? '').toLowerCase().includes(q)) : players.value
  return [...list].sort(compare)
})

const arrow = (k: SortKey) => (sortKey.value === k ? (sortDesc.value ? '↓' : '↑') : '')

async function load() {
  error.value = ''
  loading.value = true
  try {
    players.value = await api.get<Player[]>('/api/players')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the players.'
  } finally {
    loading.value = false
  }
}

const open = (p: Player) => router.push({ name: 'player', params: { id: p.id } })

onMounted(load)
</script>

<template>
  <h1>Players</h1>

  <AppAlert v-if="error" :message="error" />

  <AppCard flush>
    <template #head>
      <input v-model="search" class="search" type="search" placeholder="Search by display name or player ID" aria-label="Search players" />
      <span class="spacer" />
      <span class="dim">{{ shown.length }} of {{ players.length }} players</span>
      <button class="secondary small" :disabled="loading" @click="load">Refresh</button>
    </template>

    <p v-if="loading" class="dim pad">Loading…</p>

    <table v-else>
      <thead>
        <tr>
          <th><button class="sort" @click="sortBy('name')">Display name {{ arrow('name') }}</button></th>
          <th>Player ID</th>
          <th><button class="sort" @click="sortBy('createdAt')">Created {{ arrow('createdAt') }}</button></th>
          <th><button class="sort" @click="sortBy('lastLoginAt')">Last login {{ arrow('lastLoginAt') }}</button></th>
          <th>Sign-in</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in shown" :key="p.id" class="row" tabindex="0" @click="open(p)" @keydown.enter="open(p)">
          <td :class="{ dim: !p.displayName }">{{ playerName(p) }}</td>
          <td class="mono">{{ p.id }}</td>
          <td class="dim" :title="when(p.createdAt, 'Unknown')">{{ ago(p.createdAt, 'Unknown') }}</td>
          <td class="dim" :title="when(p.lastLoginAt)">{{ ago(p.lastLoginAt) }}</td>
          <td>
            <span v-for="pr in p.providers" :key="pr" class="chip">{{ pr }}</span>
            <span v-if="!p.providers.length" class="dim">none</span>
          </td>
        </tr>
        <tr v-if="!shown.length">
          <td colspan="5" class="dim">{{ players.length ? 'No player matches that search.' : 'No players yet.' }}</td>
        </tr>
      </tbody>
    </table>
  </AppCard>
</template>

<style scoped>
h1 { margin: 0 0 1.25rem; font-size: 1.5rem; font-weight: 600; }
.dim { color: var(--text-dim); font-size: 0.85rem; }
.pad { padding: 1rem; margin: 0; }
.spacer { flex: 1; }
.small { padding: 0.25rem 0.6rem; font-size: 0.8rem; }
.mono { font-family: var(--mono); font-size: 0.8rem; color: var(--text-dim); word-break: break-all; }
.search { max-width: 380px; padding: 0.4rem 0.6rem; }

.sort { padding: 0; border: none; background: none; color: inherit; font: inherit; font-weight: 600; }

.row { cursor: pointer; }
.row:hover td, .row:focus-visible td { background: var(--surface); }
.row:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }

.chip {
  display: inline-block;
  margin-right: 0.3rem;
  padding: 0.1rem 0.5rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.78rem;
  background: var(--surface);
}

@media (max-width: 760px) {
  th:nth-child(2), td:nth-child(2) { display: none; }
}
</style>
