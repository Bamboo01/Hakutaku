import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { currentUser, refreshSession } from './api'
import AdminsPage from './pages/AdminsPage.vue'
import HomePage from './pages/HomePage.vue'
import LoginPage from './pages/LoginPage.vue'

// `nav` puts a route in the hamburger menu, `owner` restricts it to the owner.
// Adding a future page is one entry below and nothing in App.vue.
declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    nav?: string
    owner?: boolean
  }
}

const routes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: LoginPage, meta: { public: true } },
  { path: '/', name: 'home', component: HomePage, meta: { nav: 'Home' } },
  { path: '/admins', name: 'admins', component: AdminsPage, meta: { nav: 'Admins', owner: true } },
  { path: '/:pathMatch(.*)*', redirect: { name: 'home' } },
]

// Built from the array above so the menu follows declaration order --
// router.getRoutes() sorts by path specificity instead, which isn't what we want.
export const navLinks = routes
  .filter((r) => r.meta?.nav)
  .map((r) => ({ path: r.path, label: r.meta!.nav!, ownerOnly: r.meta?.owner === true }))

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
