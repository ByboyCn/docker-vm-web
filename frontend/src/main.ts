import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import Home from './pages/Home.vue'
import Admin from './pages/Admin.vue'
import Login from './pages/Login.vue'
import { getStoredUser, api, setStoredUser } from './api'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: Home, meta: { requireAuth: true } },
    { path: '/admin', name: 'admin', component: Admin, meta: { requireAuth: true, requireAdmin: true } },
    { path: '/login', name: 'login', component: Login, meta: { public: true } },
  ],
})

// 路由守卫:进入受保护页面前,若本地无用户信息则尝试 me() 拉取
router.beforeEach(async to => {
  const isPublic = !!to.meta.public
  if (isPublic) {
    // 已登录用户访问登录页直接跳首页
    if (to.path === '/login' && getStoredUser()) {
      return { name: 'home' }
    }
    return true
  }

  // 受保护页面
  let user = getStoredUser()
  if (!user) {
    // 尝试用 cookie 里的 session 拉一次
    try {
      user = await api.me()
    } catch {
      user = null
    }
  }

  if (!user) {
    return { name: 'login' }
  }

  // 同步最新用户信息到 localStorage
  setStoredUser(user)

  // 管理员页面校验
  if (to.meta.requireAdmin && !user.isAdmin) {
    return { name: 'home' }
  }

  return true
})

createApp(App).use(router).mount('#app')
