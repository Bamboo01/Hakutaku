<script setup lang="ts">
import { ref, onMounted } from 'vue'

const players = ref<any[]>([])
const deviceId = ref('')

async function load() {
  const res = await fetch('/api/players')
  players.value = await res.json()
}

async function addPlayer() {
  await fetch('/api/players', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ deviceId: deviceId.value, xp: 0 })
  })
  deviceId.value = ''
  await load()
}

onMounted(load)
</script>

<template>
  <h1>Hakutaku</h1>

  <input v-model="deviceId" placeholder="device id" />
  <button @click="addPlayer">add</button>

  <ul>
    <li v-for="p in players" :key="p.id">
      {{ p.deviceId }} — {{ p.xp }} XP
    </li>
  </ul>
</template>