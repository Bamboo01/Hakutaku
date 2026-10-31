<!-- Server keys page.
     Owner-only: lists the keys game servers use for /api/server/*, makes a new one
     and revokes one. A new key is shown once, right after it is made, because the
     server only keeps its hash. The server enforces all three with RequireOwner,
     so the checks here are only to keep the UI honest. -->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppAlert from '../components/AppAlert.vue'
import { ApiError, api, currentUser } from '../api'

type ServerKey = {
  id: number
  name: string
  createdAt: string
  expiresAt: string | null
  revokedAt: string | null
  lastUsedAt: string | null
}

// What POST answers with. `key` is the only time it ever leaves the server.
type NewKey = { id: number; name: string; key: string }

const isOwner = computed(() => currentUser.value?.role === 'owner')

const keys = ref<ServerKey[]>([])
const loading = ref(true)
const error = ref('')

const name = ref('')
// A datetime-local value (local time, no zone). Empty means the key never expires.
const expires = ref('')
const busy = ref(false)

// The key that was just made. Kept until the owner says they have saved it.
const created = ref<NewKey | null>(null)
const copyNote = ref('')

// Matches the server's rule: a name is required (100 characters at most), and an
// expiry, if given, has to be in the future.
const expiryInPast = computed(() => expires.value !== '' && new Date(expires.value).getTime() <= Date.now())
const canCreate = computed(
  () => name.value.trim().length > 0 && name.value.trim().length <= 100 && !expiryInPast.value,
)

function fail(e: unknown, fallback: string) {
  error.value = e instanceof ApiError ? e.message : fallback
}

async function load() {
  error.value = ''
  loading.value = true
  try {
    keys.value = await api.get<ServerKey[]>('/api/admin/server-keys')
  } catch (e) {
    fail(e, 'Could not load the server keys.')
  } finally {
    loading.value = false
  }
}

async function create() {
  error.value = ''
  copyNote.value = ''
  busy.value = true
  try {
    created.value = await api.post<NewKey>('/api/admin/server-keys', {
      name: name.value.trim(),
      expiresAt: expires.value ? new Date(expires.value).toISOString() : undefined,
    })
    name.value = ''
    expires.value = ''
    await load()
  } catch (e) {
    fail(e, 'Could not make the server key.')
  } finally {
    busy.value = false
  }
}

async function copy() {
  if (!created.value) return
  try {
    await navigator.clipboard.writeText(created.value.key)
    copyNote.value = 'Copied.'
  } catch {
    // The clipboard needs https or localhost. The key is in a box that can be selected by hand.
    copyNote.value = 'Could not copy. Select the key and copy it yourself.'
  }
}

function dismiss() {
  created.value = null
  copyNote.value = ''
}

// One-way on the server: there is no restore, so a revoked key stays revoked.
async function revoke(k: ServerKey) {
  if (!confirm(`Revoke "${k.name}"?\n\nAny game server using this key stops working at once. This cannot be undone.`)) return
  error.value = ''
  try {
    await api.del(`/api/admin/server-keys/${k.id}`)
    await load()
  } catch (e) {
    fail(e, 'Could not revoke the server key.')
  }
}

const when = (iso: string) => new Date(iso).toLocaleString()
const expired = (k: ServerKey) => k.expiresAt !== null && new Date(k.expiresAt).getTime() <= Date.now()

function status(k: ServerKey): string {
  if (k.revokedAt) return `Revoked ${when(k.revokedAt)}`
  if (expired(k)) return 'Expired'
  return 'Active'
}

onMounted(() => {
  if (isOwner.value) load()
})
</script>

<template>
  <h1>Server keys</h1>

  <AppAlert v-if="!isOwner" kind="info" message="Only the owner account can manage server keys." />

  <template v-else>
    <AppAlert v-if="error" :message="error" />

    <!-- Shown once, right after a key is made, and gone for good once dismissed. -->
    <section v-if="created" class="new-key" aria-live="polite">
      <h2>Key for "{{ created.name }}"</h2>
      <p class="warn">You won't see this key again. Copy it now and keep it somewhere safe.</p>
      <div class="key-row">
        <input :value="created.key" readonly aria-label="The new server key" @focus="($event.target as HTMLInputElement).select()" />
        <button type="button" @click="copy">Copy</button>
      </div>
      <p v-if="copyNote" class="dim">{{ copyNote }}</p>
      <p class="dim">Send it in the <code>X-Server-Key</code> header on every <code>/api/server/*</code> call.</p>
      <button type="button" class="secondary" @click="dismiss">I've saved it</button>
    </section>

    <form class="create" @submit.prevent="create">
      <label>
        <span>Name</span>
        <input v-model="name" autocomplete="off" maxlength="100" placeholder="e.g. homelab" />
      </label>
      <label>
        <span>Expires (optional)</span>
        <input v-model="expires" type="datetime-local" />
      </label>
      <button type="submit" :disabled="busy || !canCreate">
        {{ busy ? 'Generating…' : 'Generate key' }}
      </button>
      <p v-if="expiryInPast" class="hint">The expiry has to be in the future.</p>
    </form>

    <p v-if="loading" class="dim">Loading…</p>

    <table v-else>
      <thead>
        <tr>
          <th>Name</th>
          <th>Created</th>
          <th>Expires</th>
          <th>Last used</th>
          <th>Status</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="k in keys" :key="k.id" :class="{ off: k.revokedAt || expired(k) }">
          <td>{{ k.name }}</td>
          <td class="dim">{{ when(k.createdAt) }}</td>
          <td class="dim">{{ k.expiresAt ? when(k.expiresAt) : 'Never' }}</td>
          <td class="dim">{{ k.lastUsedAt ? when(k.lastUsedAt) : 'Never' }}</td>
          <td>{{ status(k) }}</td>
          <td class="right">
            <!-- A revoked key has nothing left to do. An expired one can still be revoked, to tidy up. -->
            <button v-if="!k.revokedAt" class="danger" @click="revoke(k)">Revoke</button>
          </td>
        </tr>
        <tr v-if="!keys.length">
          <td colspan="6" class="dim">No server keys yet.</td>
        </tr>
      </tbody>
    </table>
  </template>
</template>

<style scoped>
h1 { margin: 0 0 1.25rem; font-size: 1.3rem; }
h2 { margin: 0 0 0.5rem; font-size: 1rem; }
.dim { color: var(--text-dim); font-size: 0.85rem; }
.right { text-align: right; }
.off td:not(.right) { opacity: 0.55; }
code { font-size: 0.85em; }

.create {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
  flex-wrap: wrap;
  margin-bottom: 1.5rem;
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface);
}
.create label { flex: 1 1 160px; margin-bottom: 0; }
.hint { flex-basis: 100%; margin: 0; color: var(--danger); font-size: 0.85rem; }

.new-key {
  margin-bottom: 1.5rem;
  padding: 1rem;
  border: 1px solid var(--accent);
  border-radius: var(--radius);
  background: var(--surface);
}
.warn { margin: 0 0 0.75rem; color: var(--danger); font-size: 0.9rem; font-weight: 600; }
.key-row { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
.key-row input { flex: 1; font-family: monospace; }
</style>
