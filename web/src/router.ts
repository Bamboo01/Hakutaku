import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { currentUser, refreshSession } from './api'
import AdminsPage from './pages/AdminsPage.vue'
import LoginPage from './pages/LoginPage.vue'
import OverviewPage from './pages/OverviewPage.vue'
import PlayerPage from './pages/PlayerPage.vue'
import PlayersPage from './pages/PlayersPage.vue'
import ServerKeysPage from './pages/ServerKeysPage.vue'

// `nav` puts a route in the sidebar, `group` files it under a foldable heading
// there (like PlayFab's Identity → Players), and `owner` restricts it to the owner.
// Adding a future page is one entry below and nothing in App.vue.
declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    nav?: string
    group?: string
    owner?: boolean
  }
}

const routes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: LoginPage, meta: { public: true } },
  { path: '/', name: 'home', component: OverviewPage, meta: { nav: 'Overview' } },
  { path: '/players', name: 'players', component: PlayersPage, meta: { nav: 'Players', group: 'Identity' } },
  // no `nav`: reached from a row of the list. `props` hands the id to the page.
  { path: '/players/:id', name: 'player', component: PlayerPage, props: true },
  { path: '/server-keys', name: 'server-keys', component: ServerKeysPage, meta: { nav: 'Server keys', group: 'Game servers', owner: true } },
  { path: '/admins', name: 'admins', component: AdminsPage, meta: { nav: 'Admins', group: 'Settings', owner: true } },
  { path: '/:pathMatch(.*)*', redirect: { name: 'home' } },
]

export type NavLink = { path: string; label: string; ownerOnly: boolean }
export type NavEntry = { group: null; link: NavLink } | { group: string; links: NavLink[] }

// Built from the array above so the menu follows declaration order --
// router.getRoutes() sorts by path specificity instead, which isn't what we want.
// A link without a group stands alone; links sharing a group gather under the
// group's first appearance.
export const navEntries: NavEntry[] = []
for (const r of routes) {
  if (!r.meta?.nav) continue
  const link = { path: r.path, label: r.meta.nav, ownerOnly: r.meta.owner === true }
  const group = r.meta.group
  if (!group) {
    navEntries.push({ group: null, link })
    continue
  }
  const existing = navEntries.find((e) => e.group === group)
  if (existing && existing.group !== null) existing.links.push(link)
  else navEntries.push({ group, links: [link] })
}

const router = createRouter({ history: createWebHistory(), routes })

router.beforeEach(async (to) => {
  // Asked only when there's no cached answer. A stale cache costs one 401 from the
  // page's own fetch, which redirects here anyway. Never poll /me on a timer:
  // every authenticated call pushes the server's 30-minute idle expiry forward.
  const user = currentUser.value ?? (await refreshSession())

  if (to.meta.public) return user ? { name: 'home' } : true
  if (!user) return { name: 'login', query: { next: to.fullPath } }
  return true
})

export default router
