<!-- A small bar chart of one daily count, for the Overview cards. One series, so no
     legend (the card's title names it), one hue (--accent), bars at most 24px thick
     with a 2px gap, a 4px rounded top and a square foot on the baseline, and a quiet
     axis. Each day's whole column is its hover and focus target, and a tooltip shows
     the day and the value. A visually hidden table carries the same numbers for
     screen readers. Hand-written SVG: no chart library. -->
<script setup lang="ts">
import { computed, ref } from 'vue'

const props = defineProps<{
  // oldest first; `endsAt` is the end of that rolling 24 hours
  days: { endsAt: string; value: number }[]
  label: string
}>()

const W = 320
const H = 140
const PAD = { top: 10, right: 6, bottom: 22, left: 30 }
const plotW = W - PAD.left - PAD.right
const plotH = H - PAD.top - PAD.bottom

// a clean top for the axis: 1, 2 or 5 times a power of ten, at least the largest value
const top = computed(() => {
  const max = Math.max(1, ...props.days.map((d) => d.value))
  const p = 10 ** Math.floor(Math.log10(max))
  return [1, 2, 5, 10].map((m) => m * p).find((v) => v >= max)!
})
const ticks = computed(() => [0, top.value / 2, top.value].filter((t) => Number.isInteger(t)))

const slot = computed(() => plotW / Math.max(1, props.days.length))
const barW = computed(() => Math.min(24, slot.value - 2))
const y = (v: number) => PAD.top + plotH - (v / top.value) * plotH

// a bar with a 4px rounded top and a square foot; nothing for zero
function barPath(i: number, v: number): string {
  if (v <= 0) return ''
  const x = PAD.left + i * slot.value + (slot.value - barW.value) / 2
  const yTop = y(v)
  const h = PAD.top + plotH - yTop
  const r = Math.min(4, barW.value / 2, h)
  const base = PAD.top + plotH
  return `M${x},${base} V${yTop + r} Q${x},${yTop} ${x + r},${yTop} H${x + barW.value - r} Q${x + barW.value},${yTop} ${x + barW.value},${yTop + r} V${base} Z`
}

// the day a window covers, named by the date it ends on
const dayName = (iso: string) => new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
const xLabels = computed(() => {
  const n = props.days.length
  if (!n) return []
  return [0, Math.floor((n - 1) / 2), n - 1].map((i) => ({ i, text: i === n - 1 ? 'Last 24h' : dayName(props.days[i].endsAt) }))
})

const hover = ref<number | null>(null)
const tip = computed(() => {
  if (hover.value === null) return null
  const d = props.days[hover.value]
  const daysAgo = props.days.length - 1 - hover.value
  const when = daysAgo === 0 ? 'Last 24 hours' : daysAgo === 1 ? '1 day ago' : `${daysAgo} days ago`
  // as a share of the box, so it lines up whatever size the svg is drawn at
  const left = ((PAD.left + (hover.value + 0.5) * slot.value) / W) * 100
  return { when, date: dayName(d.endsAt), value: d.value, left, flip: left > 60 }
})
</script>

<template>
  <div class="chart" @pointerleave="hover = null">
    <svg :viewBox="`0 0 ${W} ${H}`" role="img" :aria-label="`${label}, one bar per day, ${days.length} days`">
      <line
        v-for="t in ticks"
        :key="t"
        class="grid"
        :class="{ base: t === 0 }"
        :x1="PAD.left"
        :x2="W - PAD.right"
        :y1="y(t)"
        :y2="y(t)"
      />
      <text v-for="t in ticks" :key="`l${t}`" class="tick" :x="PAD.left - 6" :y="y(t) + 3" text-anchor="end">
        {{ t.toLocaleString() }}
      </text>

      <path v-for="(d, i) in days" :key="d.endsAt" class="bar" :class="{ lit: hover === i }" :d="barPath(i, d.value)" />

      <text
        v-for="l in xLabels"
        :key="l.i"
        class="tick"
        :x="PAD.left + (l.i + 0.5) * slot"
        :y="H - 6"
        :text-anchor="l.i === 0 ? 'start' : l.i === days.length - 1 ? 'end' : 'middle'"
      >
        {{ l.text }}
      </text>

      <!-- the hit targets: each day's whole column, wider than the bar -->
      <rect
        v-for="(d, i) in days"
        :key="`h${d.endsAt}`"
        class="hit"
        :x="PAD.left + i * slot"
        :y="PAD.top"
        :width="slot"
        :height="plotH"
        tabindex="0"
        :aria-label="`${dayName(d.endsAt)}: ${d.value}`"
        @pointerenter="hover = i"
        @focus="hover = i"
        @blur="hover = null"
      />
    </svg>

    <div v-if="tip" class="tip" :class="{ flip: tip.flip }" :style="{ left: `${tip.left}%` }" aria-hidden="true">
      <span class="when">{{ tip.when }} <span class="date">· {{ tip.date }}</span></span>
      <strong>{{ tip.value.toLocaleString() }}</strong> <span class="what">{{ label.toLowerCase() }}</span>
    </div>

    <table class="sr-only">
      <caption>{{ label }} per day</caption>
      <thead><tr><th>Day ending</th><th>{{ label }}</th></tr></thead>
      <tbody>
        <tr v-for="d in days" :key="`t${d.endsAt}`"><td>{{ dayName(d.endsAt) }}</td><td>{{ d.value }}</td></tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.chart { position: relative; }
svg { display: block; width: 100%; height: auto; overflow: visible; }

.grid { stroke: var(--border); stroke-width: 1; }
.grid.base { stroke: var(--text-dim); opacity: 0.5; }
.tick { fill: var(--text-dim); font-size: 9px; font-family: var(--sans); }

.bar { fill: var(--accent); }
.bar.lit { opacity: 0.75; }
.hit { fill: transparent; cursor: default; outline: none; }
.hit:focus-visible { stroke: var(--accent); stroke-width: 1.5; }

.tip {
  position: absolute;
  top: -0.25rem;
  transform: translate(-50%, -100%);
  padding: 0.35rem 0.55rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--bg);
  box-shadow: 0 2px 8px rgb(0 0 0 / 0.12);
  font-size: 0.78rem;
  white-space: nowrap;
  pointer-events: none;
  z-index: 1;
}
.tip.flip { transform: translate(-85%, -100%); }
.when { display: block; color: var(--text-dim); }
.date { opacity: 0.8; }
.what { color: var(--text-dim); }

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}
</style>
