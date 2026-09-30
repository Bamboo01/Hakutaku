import { createApp } from 'vue'
import App from './App.vue'
import { setUnauthorizedHandler } from './api'
import router from './router'
import './style.css'

// Any 401 from any request lands here, so pages never handle redirects themselves.
setUnauthorizedHandler(() => {
  if (router.currentRoute.value.name !== 'login') {
    router.replace({ name: 'login', query: { next: router.currentRoute.value.fullPath } })
  }
})

createApp(App).use(router).mount('#app')
