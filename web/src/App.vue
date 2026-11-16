<!-- Shell: top bar, a sidebar like PlayFab's (always open on a wide screen, foldable
     groups such as Identity → Players, a ☰ toggle on a narrow one) and <router-view>.
     The sidebar is built from the routes that declare `meta.nav`, so a new page
     needs no change here. -->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { api, currentUser } from './api'
import { type NavLink, navEntries } from './router'

const route = useRoute()
const router = useRouter()

// Only used on a narrow screen; on a wide one the sidebar is always there.
const open = ref(false)
watch(() => route.fullPath, () => (open.value = false))

// The login page renders bare -- no nav for someone who isn't signed in.
const showShell = computed(() => route.name !== 'login')

const isOwner = computed(() => currentUser.value?.role === 'owner')
const visible = (l: NavLink) => !l.ownerOnly || isOwner.value

// Entries the current user may see; a group whose links are all owner-only vanishes for an admin.
const entries = computed(() =>
  navEntries
    .map((e) => (e.group === null ? e : { ...e, links: e.links.filter(visible) }))
    .filter((e) => (e.group === null ? visible(e.link) : e.links.length > 0)),
)

// A page under a link, like /players/<id> under /players, keeps that link lit.
const isActive = (l: NavLink) => route.path === l.path || (l.path !== '/' && route.path.startsWith(l.path + '/'))

// Groups start open; folding one is remembered per browser.
const FOLD_KEY = 'hakutaku-folded-groups'
const folded = ref<string[]>(readFolded())
function readFolded(): string[] {
  try {
    const v = JSON.parse(localStorage.getItem(FOLD_KEY) ?? '[]')
    return Array.isArray(v) ? v : []
  } catch {
    return []
  }
}
function toggleGroup(g: string) {
  folded.value = folded.value.includes(g) ? folded.value.filter((x) => x !== g) : [...folded.value, g]
  try {
    localStorage.setItem(FOLD_KEY, JSON.stringify(folded.value))
  } catch {
    /* private mode: folding just isn't remembered */
  }
}

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
  router.replace({ name: 'login' })
}
</script>

<template>
  <header v-if="showShell">
    <button class="icon secondary menu" :aria-expanded="open" aria-label="Toggle menu" @click="open = !open">☰</button>
    <strong>Hakutaku</strong>
    <span class="spacer" />
    <button class="icon secondary" aria-label="Toggle dark mode" @click="toggleTheme">◐</button>
    <span class="who">{{ currentUser?.username }}</span>
    <button class="secondary" @click="logout">Log out</button>
  </header>

  <div class="layout">
    <aside v-if="showShell" :class="{ open }">
      <nav>
        <template v-for="e in entries" :key="e.group ?? e.link.path">
          <RouterLink v-if="e.group === null" :to="e.link.path" :class="{ lit: isActive(e.link) }">
            {{ e.link.label }}
          </RouterLink>
          <div v-else class="group">
            <button class="group-head" :aria-expanded="!folded.includes(e.group)" @click="toggleGroup(e.group)">
              <span>{{ e.group }}</span>
              <span class="chev" :class="{ shut: folded.includes(e.group) }">⌃</span>
            </button>
            <div v-show="!folded.includes(e.group)" class="group-links">
              <RouterLink v-for="l in e.links" :key="l.path" :to="l.path" :class="{ lit: isActive(l) }">
                {{ l.label }}
              </RouterLink>
            </div>
          </div>
        </template>
      </nav>
    </aside>
    <main :class="{ bare: !showShell }"><RouterView /></main>
  </div>
</template>

<style scoped>
header {
  position: sticky;
  top: 0;
  z-index: 3;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  height: 3.25rem;
  padding: 0 1rem;
  background: var(--surface);
  border-bottom: 1px solid var(--border);
}
.spacer { flex: 1; }
.who { font-size: 0.85rem; color: var(--text-dim); }
.icon { padding: 0.35rem 0.6rem; line-height: 1; }
.menu { display: none; }

.layout { display: flex; align-items: flex-start; }

aside {
  position: sticky;
  top: 3.25rem;
  flex: 0 0 220px;
  height: calc(100vh - 3.25rem);
  overflow-y: auto;
  border-right: 1px solid var(--border);
  padding: 1rem 0.75rem;
  background: var(--bg);
}
nav { display: flex; flex-direction: column; gap: 0.15rem; }
nav a {
  padding: 0.5rem 0.65rem;
  border-radius: var(--radius);
  color: var(--text);
  text-decoration: none;
  font-size: 0.9rem;
}
nav a:hover { background: var(--surface); }
nav a.lit { background: var(--matcha); color: #14170f; font-weight: 600; }

.group { margin-top: 0.35rem; }
.group-head {
  display: flex;
  width: 100%;
  align-items: center;
  padding: 0.5rem 0.65rem;
  border: none;
  background: transparent;
  color: var(--text);
  font-size: 0.9rem;
  text-align: left;
}
.group-head span:first-child { flex: 1; }
.group-head:hover { background: var(--surface); }
.chev { font-size: 0.8rem; color: var(--text-dim); transition: transform 0.15s; }
.chev.shut { transform: rotate(180deg); }
.group-links { display: flex; flex-direction: column; gap: 0.15rem; }
.group-links a { padding-left: 1.6rem; }

main { flex: 1; min-width: 0; padding: 1.5rem 1.75rem 3rem; }
main.bare { padding: 0; }

@media (max-width: 900px) {
  .menu { display: inline-block; }
  aside {
    display: none;
    position: fixed;
    z-index: 2;
    left: 0;
    box-shadow: 4px 0 16px rgb(0 0 0 / 0.15);
  }
  aside.open { display: block; }
  main { padding: 1.25rem 1rem 3rem; }
}
@media (max-width: 640px) {
  .who { display: none; }
}
</style>
