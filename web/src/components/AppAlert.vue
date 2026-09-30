<!-- Shared message banner: the pages' only common piece of UI. -->
<script setup lang="ts">
defineProps<{ message: string; kind?: 'error' | 'info' }>()
</script>

<template>
  <p class="alert" :class="kind ?? 'error'" role="status">{{ message }}</p>
</template>

<style scoped>
.alert {
  margin: 0 0 1rem;
  padding: 0.6rem 0.8rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--surface);
  font-size: 0.9rem;
}
.error { color: var(--danger); }
.info { color: var(--text-dim); }
</style>
