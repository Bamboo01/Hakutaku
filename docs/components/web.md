# Web UI

Vue 3 with `<script setup>` and TypeScript, built by Vite, served from the
server's `wwwroot` in production. It is the admin panel — there is no
player-facing UI.

## Layout

```text
web/src/
├── main.ts              createApp + router + the global 401 handler
├── App.vue              Shell: hamburger nav, theme toggle, <router-view>
├── router.ts            Routes, the nav menu source, the auth guard
├── api.ts               The one fetch wrapper
├── style.css            Theme tokens and element styles
├── pages/
│   ├── LoginPage.vue
│   ├── HomePage.vue     Placeholder: "UNDER CONSTRUCTION :P"
│   └── AdminsPage.vue   Owner-only admin management
└── components/
    └── AppAlert.vue     Shared message banner
```

Four conventions hold this together. They are worth keeping:

1. **All HTTP goes through `api.ts`.** No page calls `fetch` directly.
2. **One route table.** The nav menu and the auth guard both derive from it.
3. **A page per route, in `pages/`.** Anything reused across pages goes in
   `components/`.
4. **No component library.** Styling is hand-written CSS driven by custom
   properties.

!!! question "Why no Vuetify or similar?"
    The panel is three pages. A component library would be more code to learn
    and override than the ~90 lines of CSS it replaces, and the theme is a dozen
    custom properties. If the UI grows a data grid and a date picker, revisit.

## `api.ts` — the single chokepoint

Every request funnels through one function, so a dead session is handled in
exactly one place.

```ts
async function request<T>(method: string, path: string, body?: unknown, silent = false): Promise<T> {
  const res = await fetch(path, { /* ... */ })

  if (res.status === 401) {
    currentUser.value = null
    if (!silent) onUnauthorized?.()
    throw new ApiError(401, 'not logged in')
  }
  if (!res.ok) throw new ApiError(res.status, await errorText(res))

  // login, logout and delete all answer 204 with no body.
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}
```

Four details that each exist for a reason:

**The 401 handler is a callback, not an import.**

```ts
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(fn: () => void) { onUnauthorized = fn }
```

`main.ts` sets it. If `api.ts` imported the router directly and the router
imported `api.ts` for its guard, that is a circular import. The callback breaks
the cycle.

**`204` is checked explicitly.** Login, logout and delete all return no body, so
`res.json()` would throw on a successful call.

**`429` has no body**, unlike the hand-written `4xx` responses:

```ts
return res.status === 429
  ? 'Too many attempts. Wait a minute and try again.'
  : `Request failed (${res.status})`
```

**`refreshSession()` is silent**, so it does not trigger the redirect itself:

```ts
export const currentUser = ref<Me | null>(null)

export async function refreshSession(): Promise<Me | null> {
  try {
    currentUser.value = await request<Me>('GET', '/api/admin/me', undefined, true)
  } catch {
    currentUser.value = null
  }
  return currentUser.value
}
```

The router guard calls this, and the guard decides where to send you. If
`refreshSession` fired the redirect too, they would fight.

`currentUser` is a module-level `ref`, which makes it reactive app-wide —
`App.vue` reads it for the username and to filter the nav, `AdminsPage` reads it
to check the role.

!!! note "`ApiError` declares its field the long way"
    ```ts
    export class ApiError extends Error {
      // Written out rather than a parameter property: tsconfig sets erasableSyntaxOnly.
      status: number
      constructor(status: number, message: string) {
        super(message)
        this.status = status
      }
    }
    ```
    `constructor(public status: number)` would be shorter and **will not
    compile** — see [the strict settings](#the-strict-typescript-settings).

## `router.ts` — routes, nav and the guard

Route metadata is typed by augmenting vue-router's interface:

```ts
declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    nav?: string
    owner?: boolean
  }
}
```

So `meta: { nav: 'Admins', owner: true }` is checked rather than stringly-typed.

```ts
const routes: RouteRecordRaw[] = [
  { path: '/login',  name: 'login',  component: LoginPage,  meta: { public: true } },
  { path: '/',       name: 'home',   component: HomePage,   meta: { nav: 'Home' } },
  { path: '/admins', name: 'admins', component: AdminsPage, meta: { nav: 'Admins', owner: true } },
  { path: '/:pathMatch(.*)*', redirect: { name: 'home' } },
]
```

### The nav menu comes from the array, not the router

```ts
export const navLinks = routes
  .filter((r) => r.meta?.nav)
  .map((r) => ({ path: r.path, label: r.meta!.nav!, ownerOnly: r.meta?.owner === true }))
```

!!! warning "Do not rewrite this as `router.getRoutes()`"
    `getRoutes()` returns routes sorted by **path specificity**, not declaration
    order, so the menu would come out in an arbitrary order — "Admins" before
    "Home". Deriving from the source array keeps the menu in the order you wrote
    it.

### The guard

```ts
router.beforeEach(async (to) => {
  const user = currentUser.value ?? (await refreshSession())

  if (to.meta.public) return user ? { name: 'home' } : true
  if (!user) return { name: 'login', query: { next: to.fullPath } }
  return true
})
```

- `/me` is asked **only when there is no cached answer** — never on a timer. A
  stale cache costs one `401` from the page's own fetch, which redirects anyway.
- A signed-in user hitting `/login` is bounced to home.
- An anonymous user is sent to `/login` with `?next=`, which `LoginPage`
  honours after a successful sign-in.

!!! danger "The guard is cosmetic — the server is the authority"
    A route guard runs in the browser and can be bypassed with `curl`, dev tools
    or a hand-built request. Every endpoint it protects is independently guarded
    server-side by `.RequireAdmin()` or `RequireOwner`. **Never** treat a
    frontend check as enforcement; it exists to keep the UI coherent.

## `App.vue` — the shell

The hamburger nav, the user label, the theme toggle, logout, and
`<router-view>`.

```ts
const showShell = computed(() => route.name !== 'login')
const links = computed(() =>
  navLinks.filter((l) => !l.ownerOnly || currentUser.value?.role === 'owner'))
```

The login page renders bare — no nav for someone who is not signed in. Adding a
page needs **no change to this file**, because the menu is derived.

Theme preference is persisted in `localStorage` under `hakutaku-theme`, and
absent a stored choice the OS preference wins:

```ts
function toggleTheme() {
  const dark = forced.value ? forced.value === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches
  forced.value = dark ? 'light' : 'dark'
  localStorage.setItem(THEME_KEY, forced.value)
  apply()
}
```

Logout swallows a `401`, because "the session was already gone" and "we just
ended it" have the same outcome.

## The pages

### `LoginPage.vue`

Username and password, posts to `/api/admin/login`, calls `refreshSession()`,
then honours `?next=` or falls back to home. Errors surface through `AppAlert`.

```ts
await api.post('/api/admin/login', { username: username.value, password: password.value })
await refreshSession()
```

Login is by **username**, not email.

### `HomePage.vue`

The landing page after sign-in. Currently a placeholder reading
`UNDER CONSTRUCTION :P` — graphs and summary data are meant to go here.

### `AdminsPage.vue`

Owner-only. Lists every admin including deactivated ones, creates admins, and
deactivates them. The file header records authorship and the key constraint:

```html
<!-- Admins page - done by Sherwyn.
     Owner-only: lists every admin (including deactivated ones), creates new
     admins, and deactivates them. The server enforces all three with
     RequireOwner, so the checks here are only to keep the UI honest. -->
```

The client-side rules mirror the server's rather than replacing them:

```ts
const canCreate = computed(() => username.value.trim().length > 0 && password.value.length >= 8)
```

Deactivation warns about the part that is not obvious:

```ts
if (!confirm(`Deactivate "${a.username}"?\n\nThis cannot be undone, and the username can never be reused.`)) return
```

The Deactivate button is hidden for the owner and for already-disabled admins —
the server would answer `403` and there is nothing left to do, respectively.

## Styling

`style.css` defines the theme as custom properties, and dark mode redefines the
same names:

```css
:root {
  --matcha: #c3e1b5;
  --bg: #ffffff;
  --surface: #f4f9f0;
  --accent: #5c8a48;
  --text: #14170f;
  /* ... */
}
```

!!! warning "`#c3e1b5` is not a text colour"
    It hits only **1.4:1** against white, far below WCAG's 4.5:1 minimum. So the
    brand green is used for *surfaces and borders*, with near-black text on top,
    and `--accent` (`#5c8a48`) is a darkened matcha used wherever light text sits
    on it. Do not swap one for the other.

Dark mode follows the OS unless the toggle has forced a theme, which needs the
rule written twice — once guarded under the media query, once for the explicit
override:

```css
@media (prefers-color-scheme: dark) {
  :root:not([data-theme='light']) { /* dark tokens */ }
}
:root[data-theme='dark'] { /* the same dark tokens */ }
```

Page-specific CSS lives in that page's `<style scoped>` block.

## The strict TypeScript settings

`npm run build` runs `vue-tsc -b` before Vite, so a type error fails the build.
`tsconfig.app.json` turns on four checks beyond the defaults:

| Setting | Effect |
|---|---|
| `noUnusedLocals` | An unused variable is an error |
| `noUnusedParameters` | An unused parameter is an error |
| `noFallthroughCasesInSwitch` | A `case` without `break` is an error |
| `erasableSyntaxOnly` | **Bans TypeScript syntax that has runtime behaviour** |

That last one is the one that catches people. It rejects constructor parameter
properties, `enum`, and namespaces — anything that is not purely erasable type
annotation:

```ts
// error TS1294: This syntax is not allowed when 'erasableSyntaxOnly' is enabled
constructor(public status: number, message: string) { super(message) }

// fine
status: number
constructor(status: number, message: string) { super(message); this.status = status }
```

Use a `const` object with a union type instead of an `enum`.

## Adding a page

1. Create `pages/YourPage.vue`.
2. Add one entry to the `routes` array in `router.ts`:
   ```ts
   { path: '/thing', name: 'thing', component: ThingPage, meta: { nav: 'Thing' } }
   ```
   Add `owner: true` if it is owner-only.
3. **Stop.** The nav menu and the guard pick it up automatically. `App.vue`
   needs no change.
4. Fetch through `api.ts` — `api.get`, `api.post`, `api.del` — never `fetch`.
5. Surface errors with `AppAlert`, and remember the server is the real guard.

## Known rough edges

- **`meta.public` means two things**: "no auth required" *and* "bounce
  signed-in users away". That is correct for `/login`, and wrong for the first
  genuinely public page someone adds. Split it into `public` and `guestOnly`
  when that happens.
- **No tests.** No Vitest, no component tests.
- **`confirm()` for destructive actions** rather than a styled modal.
- **No loading skeletons** — just a "Loading…" line.
- **`index.html` is cacheable**, so after a deploy a returning browser can run
  the previous bundle from cache. Hashed asset filenames make the fix easy if it
  ever matters: `Cache-Control: no-store` on `index.html` only.
