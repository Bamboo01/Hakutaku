<!-- Overview page, the landing page after sign-in, like PlayFab's Title Overview.
     Reads GET /api/admin/stats once and shows cards for unique users, new users
     and logins: an Amount / Change table for the last 24 hours and 30 days (change
     against the period before, "NA" when there was nothing to compare with), and a
     bar per day for the chosen time period. A day is a rolling 24 hours counted back
     from when the numbers were made, the same in every timezone. Any admin. -->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppAlert from '../components/AppAlert.vue'
import AppCard from '../components/AppCard.vue'
import DailyBars from '../components/DailyBars.vue'
import { ApiError, api } from '../api'

type Window = { last24h: number; previous24h: number; last30Days: number; previous30Days: number }
type Day = { endsAt: string; uniqueUsers: number; newUsers: number; logins: number }
type Stats = {
  generatedAt: string
  totalPlayers: number
  uniqueUsers: Window
  newUsers: Window
  logins: Window
  days: Day[]
}
type Metric = 'uniqueUsers' | 'newUsers' | 'logins'

const CARDS: { metric: Metric; title: string; note: string }[] = [
  { metric: 'uniqueUsers', title: 'Unique users', note: 'Players who signed in at least once.' },
  { metric: 'newUsers', title: 'New users', note: 'Players created.' },
  { metric: 'logins', title: 'Logins', note: 'Sessions started, registering included.' },
]

const PERIODS = [
  { days: 7, label: 'Last 7 days' },
  { days: 14, label: 'Last 14 days' },
  { days: 30, label: 'Last 30 days' },
]
const PERIOD_KEY = 'hakutaku-overview-period'
const period = ref<number>(readPeriod())
function readPeriod(): number {
  try {
    const v = Number(localStorage.getItem(PERIOD_KEY))
    return PERIODS.some((p) => p.days === v) ? v : 7
  } catch {
    return 7
  }
}
function setPeriod(v: number) {
  period.value = v
  try {
    localStorage.setItem(PERIOD_KEY, String(v))
  } catch {
    /* not remembered, that's all */
  }
}

const stats = ref<Stats | null>(null)
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  try {
    stats.value = await api.get<Stats>('/api/admin/stats')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load the numbers.'
  } finally {
    loading.value = false
  }
}

const series = (m: Metric) =>
  (stats.value?.days ?? []).slice(-period.value).map((d) => ({ endsAt: d.endsAt, value: d[m] }))

// PlayFab's Change column: the difference against the period before, as a percentage.
// With nothing before there is no fair percentage, so it says NA.
function change(now: number, before: number): { text: string; dir: 'up' | 'down' | 'flat' } {
  if (before === 0) return { text: 'NA', dir: 'flat' }
  const pct = Math.round(((now - before) / before) * 100)
  if (pct === 0) return { text: '0%', dir: 'flat' }
  return { text: `${pct > 0 ? '+' : ''}${pct}%`, dir: pct > 0 ? 'up' : 'down' }
}

const updated = computed(() => (stats.value ? new Date(stats.value.generatedAt).toLocaleTimeString() : ''))

onMounted(load)
</script>

<template>
  <h1>Overview</h1>

  <div class="controls">
    <label>
      <span>Time period</span>
      <select :value="period" @change="setPeriod(Number(($event.target as HTMLSelectElement).value))">
        <option v-for="p in PERIODS" :key="p.days" :value="p.days">{{ p.label }}</option>
      </select>
    </label>
    <span class="spacer" />
    <span v-if="updated" class="dim">Updated {{ updated }}</span>
    <button class="secondary small" :disabled="loading" @click="load">{{ loading ? 'Loading…' : 'Refresh' }}</button>
  </div>

  <AppAlert v-if="error" :message="error" />

  <p v-if="loading && !stats" class="dim">Loading…</p>

  <template v-if="stats">
    <div class="grid">
      <AppCard title="Players">
        <p class="hero">{{ stats.totalPlayers.toLocaleString() }}</p>
        <p class="dim">players in total</p>
        <RouterLink to="/players" class="more">View players →</RouterLink>
      </AppCard>

      <AppCard v-for="c in CARDS" :key="c.metric" flush>
        <template #head>
          <h2 class="col-title">{{ c.title }}</h2>
          <span class="col">Amount</span>
          <span class="col">Change</span>
        </template>

        <table class="figures">
          <tbody>
            <tr v-for="row in [
              { label: 'Last 24 hours', now: stats[c.metric].last24h, before: stats[c.metric].previous24h },
              { label: 'Last 30 days', now: stats[c.metric].last30Days, before: stats[c.metric].previous30Days },
            ]" :key="row.label">
              <th scope="row">{{ row.label }}</th>
              <td class="col">{{ row.now.toLocaleString() }}</td>
              <td class="col">
                <span :class="['chg', change(row.now, row.before).dir]">
                  <span v-if="change(row.now, row.before).dir === 'up'" aria-hidden="true">▲ </span>
                  <span v-else-if="change(row.now, row.before).dir === 'down'" aria-hidden="true">▼ </span>
                  {{ change(row.now, row.before).text }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>

        <div class="plot">
          <DailyBars :days="series(c.metric)" :label="c.title" />
          <p class="note dim">{{ c.note }} One bar per 24 hours.</p>
        </div>
      </AppCard>
    </div>
  </template>
</template>

<style scoped>
h1 { margin: 0 0 1.25rem; font-size: 1.5rem; font-weight: 600; }
.dim { color: var(--text-dim); font-size: 0.85rem; }
.spacer { flex: 1; }
.small { padding: 0.25rem 0.6rem; font-size: 0.8rem; }

.controls { display: flex; align-items: flex-end; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 1.25rem; }
.controls label { margin: 0; }
select {
  font: inherit;
  padding: 0.4rem 0.6rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg);
  color: var(--text);
}

.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(330px, 1fr)); gap: 1.25rem; align-items: start; }

.hero { margin: 0.25rem 0 0; font-size: 2.6rem; font-weight: 600; font-variant-numeric: tabular-nums; line-height: 1.1; }
.more { display: inline-block; margin-top: 0.75rem; font-size: 0.85rem; color: var(--accent); }

/* the card head doubles as the table's header row, as on PlayFab */
.col-title { margin: 0; font-size: 1rem; font-weight: 600; flex: 1; }
.col { width: 5.5rem; text-align: right; font-variant-numeric: tabular-nums; }
:deep(header) .col { font-size: 0.85rem; font-weight: 600; color: var(--text-dim); }

.figures th { font-weight: 600; font-size: 0.9rem; color: var(--text); }
.figures td.col { padding-right: 1rem; }
.chg { font-size: 0.85rem; }
.chg.flat { color: var(--text-dim); }
.chg.up, .chg.down { color: var(--text); }

.plot { padding: 1rem 1rem 0.75rem; border-top: 1px solid var(--border); }
.note { margin: 0.5rem 0 0; font-size: 0.78rem; }
</style>
