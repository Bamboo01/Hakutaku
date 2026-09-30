import { createRouter, createWebHistory } from 'vue-router'
import { currentUser, refreshSession } from './api'
import AdminsPage from './pages/AdminsPage.vue'
import LoginPage from './pages/LoginPage.vue'

// `nav` puts a route in the hamburger menu automatically, so adding a future
// page is one entry here and nothing in App.vue.
declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    nav?: string
    owner?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: { name: 'admins' } },
    { path: '/login', name: 'login', component: LoginPage, meta: { public: true } },
    { path: '/admins', name: 'admins', component: AdminsPage, meta: { nav: 'Admins', owner: true } },
    { path: '/:pathMatch(.*)*', redirect: { name: 'admins' } },
  ],
})

router.beforeEach(async (to) => {
  // Asked only when there's no cached answer. A stale cache costs one 401 from the
  // page's own fetch, which redirects here anyway. Never poll /me on a timer:
  // every authenticated call pushes the server's 30-minute idle expiry forward.
  const user = currentUser.value ?? (await refreshSession())

  if (to.meta.public) return user ? { name: 'admins' } : true
  if (!user) return { name: 'login', query: { next: to.fullPath } }
  return true
})

export default router
