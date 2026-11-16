<!-- Players page.
     Lists every player (the server answers with the whole table, no paging) with
     a search over name and id. A row opens that player's page, where their
     player data is. Any signed-in admin may use it. -->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppAlert from '../components/AppAlert.vue'
import { ApiError, api } from '../api'
import { type Player, playerName } from '../playerData'

const router = useRouter()

const players = ref<Player[]>([])
const loading = ref(true)
const error = ref('')
const search = ref('')

const shown = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return players.value
  return players.value.filter((p) => p.id.includes(q) || (p.displayName ?? '').toLowerCase().includes(q))
})

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

  <div class="toolbar">
    <input v-model="search" type="search" placeholder="Search by display name or player id" aria-label="Search players" />
    <span class="dim">{{ shown.length }} of {{ players.length }}</span>
  </div>

  <p v-if="loading" class="dim">Loading…</p>

  <table v-else>
    <thead>
      <tr>
        <th>Display name</th>
        <th>Player id</th>
        <th>Sign-in</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="p in shown" :key="p.id" class="row" tabindex="0" @click="open(p)" @keydown.enter="open(p)">
        <td :class="{ dim: !p.displayName }">{{ playerName(p) }}</td>
        <td class="mono">{{ p.id }}</td>
        <td>
          <span v-for="pr in p.providers" :key="pr" class="chip">{{ pr }}</span>
          <span v-if="!p.providers.length" class="dim">none</span>
        </td>
      </tr>
      <tr v-if="!shown.length">
        <td colspan="3" class="dim">{{ players.length ? 'No player matches that search.' : 'No players yet.' }}</td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
h1 { margin: 0 0 1.25rem; font-size: 1.3rem; }
.dim { color: var(--text-dim); font-size: 0.85rem; }
.mono { font-family: var(--mono); font-size: 0.82rem; color: var(--text-dim); word-break: break-all; }

.toolbar { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1rem; }
.toolbar input { max-width: 420px; }

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
</style>
