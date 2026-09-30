<!-- Shell: hamburger nav + <router-view>. The menu is built from the routes that
     declare `meta.nav`, so a new page needs no change here. -->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { api, currentUser } from './api'

const route = useRoute()
const router = useRouter()
const open = ref(false)

// The login page renders bare -- no nav for someone who isn't signed in.
const showShell = computed(() => route.name !== 'login')

const links = computed(() =>
  router.getRoutes().filter((r) => r.meta.nav && (!r.meta.owner || currentUser.value?.role === 'owner')),
)

const THEME_KEY = 'hakutaku-theme'
const forced = ref<'light' | 'dark' | null>(
  ((v) => (v === 'light' || v === 'dark' ? v : null))(localStorage.getItem(THEME_KEY)),
)
apply()

function apply() {
  if (forced.value) document.documentElement.dataset.theme = forced.value
  else delete document.documentElement.dataset.theme
}

function toggleTheme() {
  const dark = forced.value ? forced.value === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches
  forced.value = dark ? 'light' : 'dark'
  localStorage.setItem(THEME_KEY, forced.value)
  apply()
}

async function logout() {
  // A 401 here just means the session was already gone; the outcome is the same.
  try {
    await api.post('/api/admin/logout')
  } catch {
    /* already logged out */
  }
  currentUser.value = null
  open.value = false
  router.replace({ name: 'login' })
}
</script>

<template>
  <header v-if="showShell">
    <button class="icon secondary" :aria-expanded="open" aria-label="Toggle menu" @click="open = !open">☰</button>
    <strong>Hakutaku</strong>
    <span class="spacer" />
    <button class="icon secondary" aria-label="Toggle dark mode" @click="toggleTheme">◐</button>
    <span class="who">{{ currentUser?.username }}</span>
    <button class="secondary" @click="logout">Log out</button>
  </header>

  <div class="layout">
    <aside v-if="showShell && open">
      <nav>
        <RouterLink v-for="l in links" :key="l.path" :to="l.path" @click="open = false">
          {{ l.meta.nav }}
        </RouterLink>
      </nav>
    </aside>
    <main :class="{ bare: !showShell }"><RouterView /></main>
  </div>
</template>

<style scoped>
header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.6rem 1rem;
  background: var(--surface);
  border-bottom: 1px solid var(--border);
}
.spacer { flex: 1; }
.who { font-size: 0.85rem; color: var(--text-dim); }
.icon { padding: 0.35rem 0.6rem; line-height: 1; }

.layout { display: flex; align-items: flex-start; }

aside {
  flex: 0 0 200px;
  border-right: 1px solid var(--border);
  min-height: calc(100vh - 3.25rem);
  padding: 1rem 0.75rem;
}
nav { display: flex; flex-direction: column; gap: 0.25rem; }
nav a {
  padding: 0.5rem 0.65rem;
  border-radius: var(--radius);
  color: var(--text);
  text-decoration: none;
  font-size: 0.9rem;
}
nav a:hover { background: var(--bg); }
nav a.router-link-active { background: var(--matcha); color: #14170f; }

main { flex: 1; min-width: 0; padding: 1.5rem 1rem; }
main.bare { padding: 0; }

@media (max-width: 640px) {
  aside { position: absolute; z-index: 1; background: var(--surface); }
  .who { display: none; }
}
</style>
