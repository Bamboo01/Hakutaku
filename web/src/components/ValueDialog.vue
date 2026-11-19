<!-- The bigger editor for one player data value: a key (only when adding), a
     monospace text area, a byte counter against the 16 KB limit and, for JSON,
     format and compact buttons. "Apply" only changes the page's unsaved edits;
     nothing reaches the server until the page's Save. -->
<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { LIMITS, byteLength, formatBytes, keyProblem, parseJson, valueProblem } from '../playerData'

const props = defineProps<{
  // null closes the dialog. `adding` lets the key be typed; otherwise it is fixed.
  open: { key: string; value: string; adding: boolean } | null
  // keys the tier already has, so adding one twice is caught here
  taken: string[]
}>()

const emit = defineEmits<{ apply: [key: string, value: string]; close: [] }>()

const dialog = ref<HTMLDialogElement | null>(null)
const key = ref('')
const value = ref('')
const keyField = ref<HTMLInputElement | null>(null)
const valueField = ref<HTMLTextAreaElement | null>(null)

watch(
  () => props.open,
  async (o) => {
    if (!o) {
      dialog.value?.close()
      return
    }
    key.value = o.key
    value.value = o.value
    dialog.value?.showModal()
    await nextTick()
    ;(o.adding ? keyField.value : valueField.value)?.focus()
  },
)

const keyError = computed(() => {
  if (!props.open?.adding) return null
  return keyProblem(key.value) ?? (props.taken.includes(key.value) ? 'This tier already has that key. Edit it instead.' : null)
})
const valueError = computed(() => valueProblem(value.value))
const bytes = computed(() => byteLength(value.value))
const json = computed(() => parseJson(value.value))

const format = () => (value.value = JSON.stringify(json.value, null, 2))
const compact = () => (value.value = JSON.stringify(json.value))

function apply() {
  if (keyError.value || valueError.value) return
  emit('apply', key.value, value.value)
}
</script>

<template>
  <dialog ref="dialog" @close="emit('close')" @cancel.prevent="emit('close')">
    <form v-if="open" method="dialog" @submit.prevent="apply">
      <h2>{{ open.adding ? 'Add a key' : 'Edit value' }}</h2>

      <label v-if="open.adding">
        <span>Key</span>
        <input ref="keyField" v-model="key" autocomplete="off" spellcheck="false" :maxlength="LIMITS.keyMaxLength" placeholder="e.g. Xp" />
        <small v-if="key && keyError" class="err">{{ keyError }}</small>
      </label>
      <p v-else class="key"><span class="dim">Key</span> <code>{{ open.key }}</code></p>

      <label>
        <span class="row">
          Value
          <span class="spacer" />
          <template v-if="json !== undefined">
            <button type="button" class="secondary small" @click="format">Format JSON</button>
            <button type="button" class="secondary small" @click="compact">Compact</button>
          </template>
        </span>
        <textarea ref="valueField" v-model="value" spellcheck="false" rows="14" />
      </label>

      <p class="meta">
        <span :class="{ err: bytes > LIMITS.valueMaxBytes }">{{ formatBytes(bytes) }} of {{ LIMITS.valueMaxBytes / 1024 }} KB</span>
        <span v-if="json !== undefined" class="dim">· valid JSON</span>
        <span v-if="valueError" class="err">· {{ valueError }}</span>
      </p>

      <div class="actions">
        <button type="button" class="secondary" @click="emit('close')">Cancel</button>
        <button type="submit" :disabled="!!keyError || !!valueError">Apply</button>
      </div>
      <p class="dim note">Applied edits are saved with the page's Save button.</p>
    </form>
  </dialog>
</template>

<style scoped>
dialog {
  width: min(760px, calc(100vw - 2rem));
  padding: 1.25rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg);
  color: var(--text);
}
dialog::backdrop { background: rgb(0 0 0 / 0.45); }
h2 { margin: 0 0 1rem; font-size: 1.05rem; }
.key { margin: 0 0 1rem; }
.row { display: flex; align-items: center; gap: 0.4rem; }
.spacer { flex: 1; }
.small { padding: 0.2rem 0.55rem; font-size: 0.8rem; }

textarea {
  width: 100%;
  font: 0.85rem/1.45 var(--mono);
  padding: 0.6rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface);
  color: var(--text);
  resize: vertical;
}
textarea:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }

.meta { margin: -0.5rem 0 1rem; font-size: 0.8rem; color: var(--text-dim); }
.err { color: var(--danger); font-size: 0.8rem; }
small.err { display: block; margin-top: 0.3rem; }
.dim { color: var(--text-dim); }
.note { margin: 0.75rem 0 0; font-size: 0.78rem; }
.actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
</style>
