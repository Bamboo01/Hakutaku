<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppAlert from '../components/AppAlert.vue'
import { ApiError, api, refreshSession } from '../api'

const route = useRoute()
const router = useRouter()

const username = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

async function submit() {
  error.value = ''
  busy.value = true
  try {
    // Login is by username, not email, and answers 204 with the cookie in a header.
    await api.post('/api/admin/login', { username: username.value, password: password.value })
    await refreshSession()
    const next = route.query.next
    await router.replace(typeof next === 'string' ? next : { name: 'admins' })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not reach the server.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="wrap">
    <form @submit.prevent="submit">
      <h1>Hakutaku</h1>
      <p class="sub">Admin sign-in</p>

      <AppAlert v-if="error" :message="error" />

      <label>
        <span>Username</span>
        <input v-model="username" autocomplete="username" autofocus required />
      </label>
      <label>
        <span>Password</span>
        <input v-model="password" type="password" autocomplete="current-password" required />
      </label>

      <button type="submit" :disabled="busy || !username || !password">
        {{ busy ? 'Signing in…' : 'Sign in' }}
      </button>
    </form>
  </div>
</template>

<style scoped>
.wrap { display: grid; place-items: center; min-height: 100vh; padding: 1rem; }
form {
  width: 100%;
  max-width: 340px;
  padding: 1.75rem;
  border: 1px solid var(--border);
  border-top: 4px solid var(--matcha);
  border-radius: var(--radius);
  background: var(--surface);
}
h1 { margin: 0; font-size: 1.3rem; }
.sub { margin: 0.2rem 0 1.5rem; font-size: 0.85rem; color: var(--text-dim); }
button { width: 100%; }
</style>
