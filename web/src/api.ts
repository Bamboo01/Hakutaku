// Every request goes through here, so a dead session is handled in exactly one
// place. The session cookie is HttpOnly, so JS can never read it -- asking
// /api/admin/me is the only way to know whether we are logged in.
import { ref } from 'vue'

export type Me = { username: string; email: string | null; role: 'owner' | 'admin' }

export class ApiError extends Error {
  // Written out rather than a parameter property: tsconfig sets erasableSyntaxOnly.
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Set by main.ts to bounce to /login. Kept as a callback so api.ts doesn't import the router. */
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn
}

/** The server's view of who we are. Null means logged out. */
export const currentUser = ref<Me | null>(null)

async function request<T>(method: string, path: string, body?: unknown, silent = false): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

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

// 400/403/404/409 answer { error }, but the login rate limiter's 429 has an empty body.
async function errorText(res: Response): Promise<string> {
  try {
    const data = await res.json()
    if (data?.error) return data.error as string
  } catch {
    /* no body */
  }
  return res.status === 429 ? 'Too many attempts. Wait a minute and try again.' : `Request failed (${res.status})`
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T = void>(path: string, body?: unknown) => request<T>('POST', path, body),
  del: <T = void>(path: string) => request<T>('DELETE', path),
}

/** Silent: the router guard decides where to send you, so it must not trigger the redirect itself. */
export async function refreshSession(): Promise<Me | null> {
  try {
    currentUser.value = await request<Me>('GET', '/api/admin/me', undefined, true)
  } catch {
    currentUser.value = null
  }
  return currentUser.value
}
