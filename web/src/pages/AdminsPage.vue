<!-- Admins page - done by Sherwyn.
     Owner-only: lists every admin (including deactivated ones), creates new
     admins, and deactivates them. The server enforces all three with
     RequireOwner, so the checks here are only to keep the UI honest. -->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppAlert from '../components/AppAlert.vue'
import { ApiError, api, currentUser } from '../api'

type Admin = {
  id: number
  username: string
  role: 'owner' | 'admin'
  createdAt: string
  disabledAt: string | null
}

const isOwner = computed(() => currentUser.value?.role === 'owner')

const admins = ref<Admin[]>([])
const loading = ref(true)
const error = ref('')

const username = ref('')
const password = ref('')
const busy = ref(false)

// Matches the server's rule: username required, password 8-256 characters.
const canCreate = computed(() => username.value.trim().length > 0 && password.value.length >= 8)

function fail(e: unknown, fallback: string) {
  error.value = e instanceof ApiError ? e.message : fallback
}

async function load() {
  error.value = ''
  loading.value = true
  try {
    admins.value = await api.get<Admin[]>('/api/admin/admins')
  } catch (e) {
    fail(e, 'Could not load admins.')
  } finally {
    loading.value = false
  }
}

async function create() {
  error.value = ''
  busy.value = true
  try {
    await api.post('/api/admin/admins', { username: username.value.trim(), password: password.value })
    username.value = ''
    password.value = ''
    await load()
  } catch (e) {
    fail(e, 'Could not create the admin.')
  } finally {
    busy.value = false
  }
}

// One-way on the server: there is no restore endpoint, and the unique index
// covers disabled rows, so this also burns the username for good.
async function deactivate(a: Admin) {
  if (!confirm(`Deactivate "${a.username}"?\n\nThis cannot be undone, and the username can never be reused.`)) return
  error.value = ''
  try {
    await api.del(`/api/admin/admins/${a.id}`)
    await load()
  } catch (e) {
    fail(e, 'Could not deactivate the admin.')
  }
}

const when = (iso: string) => new Date(iso).toLocaleString()

onMounted(() => {
  if (isOwner.value) load()
})
</script>

<template>
  <h1>Admins</h1>

  <AppAlert v-if="!isOwner" kind="info" message="Only the owner account can manage admins." />

  <template v-else>
    <AppAlert v-if="error" :message="error" />

    <form class="create" @submit.prevent="create">
      <label>
        <span>Username</span>
        <input v-model="username" autocomplete="off" />
      </label>
      <label>
        <span>Password (min 8)</span>
        <input v-model="password" type="password" autocomplete="new-password" />
      </label>
      <button type="submit" :disabled="busy || !canCreate">
        {{ busy ? 'Creating…' : 'Create admin' }}
      </button>
    </form>

    <p v-if="loading" class="dim">Loading…</p>

    <table v-else>
      <thead>
        <tr>
          <th>Username</th>
          <th>Role</th>
          <th>Created</th>
          <th>Status</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="a in admins" :key="a.id" :class="{ off: a.disabledAt }">
          <td>{{ a.username }}</td>
          <td>{{ a.role }}</td>
          <td class="dim">{{ when(a.createdAt) }}</td>
          <td>{{ a.disabledAt ? `Deactivated ${when(a.disabledAt)}` : 'Active' }}</td>
          <td class="right">
            <!-- The owner can't be deactivated (server answers 403), and a
                 deactivated admin has nothing left to do. -->
            <button
              v-if="a.role !== 'owner' && !a.disabledAt"
              class="danger"
              @click="deactivate(a)"
            >
              Deactivate
            </button>
          </td>
        </tr>
        <tr v-if="!admins.length">
          <td colspan="5" class="dim">No admins yet.</td>
        </tr>
      </tbody>
    </table>
  </template>
</template>

<style scoped>
h1 { margin: 0 0 1.25rem; font-size: 1.3rem; }
.dim { color: var(--text-dim); font-size: 0.85rem; }
.right { text-align: right; }
.off td:not(.right) { opacity: 0.55; }

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
</style>
