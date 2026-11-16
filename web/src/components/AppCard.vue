<!-- A panel with a header bar, like PlayFab's cards: a title (and anything in the
     `head` slot, such as filters or buttons) on a tinted bar, then the body. A
     table put straight in the body runs edge to edge. -->
<script setup lang="ts">
defineProps<{ title?: string; flush?: boolean }>()
</script>

<template>
  <section class="card">
    <header v-if="title || $slots.head">
      <h2 v-if="title">{{ title }}</h2>
      <slot name="head" />
    </header>
    <div class="body" :class="{ flush }"><slot /></div>
  </section>
</template>

<style scoped>
.card {
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  overflow: hidden;
}
header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  min-height: 3rem;
  padding: 0.5rem 1rem;
  background: var(--surface);
  border-bottom: 1px solid var(--border);
}
h2 { margin: 0; font-size: 1rem; font-weight: 600; flex: 1; }
.body { padding: 1rem; }
.body.flush { padding: 0; }
.body.flush :deep(table) { margin: 0; }
.body.flush :deep(th:first-child), .body.flush :deep(td:first-child) { padding-left: 1rem; }
.body.flush :deep(tr:last-child td) { border-bottom: none; }
</style>
