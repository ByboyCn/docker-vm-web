<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NMessageProvider, NDialogProvider, NConfigProvider, zhCN, dateZhCN, NEl,
  NDropdown, NSpace, NTag,
} from 'naive-ui'
import { api, getStoredUser, clearStoredUser, type UserDto } from './api'

const route = useRoute()
const router = useRouter()

const user = ref<UserDto | null>(getStoredUser())

// 每次路由变化刷新当前用户(localStorage 可能被其他 tab 改了)
router.afterEach(() => {
  user.value = getStoredUser()
})

// 首次加载时尝试拉一次最新用户信息
onMounted(async () => {
  try {
    const u = await api.me()
    user.value = u
  } catch {
    user.value = null
  }
})

const isLogin = computed(() => route.name === 'login')

const userMenu = computed(() => {
  const items: any[] = [
    { label: '退出登录', key: 'logout' },
  ]
  if (user.value?.isAdmin) {
    items.unshift({ label: '管理后台', key: 'admin' })
  }
  return items
})

async function onUserMenu(key: string) {
  if (key === 'logout') {
    try {
      await api.logout()
    } catch { /* ignore */ }
    clearStoredUser()
    user.value = null
    router.push('/login')
  } else if (key === 'admin') {
    router.push('/admin')
  }
}

function selectUserDropdown(key: string) {
  onUserMenu(key)
}
</script>

<template>
  <n-config-provider :locale="zhCN" :date-locale="dateZhCN">
    <n-message-provider>
      <n-dialog-provider>
        <div class="layout">
          <header v-if="!isLogin" class="header">
            <div class="brand">🐳 Docker 虚拟机</div>
            <nav class="nav">
              <router-link to="/" class="nav-link">首页</router-link>
              <router-link v-if="user?.isAdmin" to="/admin" class="nav-link">管理后台</router-link>
              <n-dropdown
                v-if="user"
                trigger="click"
                :options="userMenu"
                @select="selectUserDropdown"
              >
                <n-space align="center" :size="6" class="user-box">
                  <span class="user-name">{{ user.username }}</span>
                  <n-tag v-if="user.isAdmin" type="warning" size="small" round>admin</n-tag>
                </n-space>
              </n-dropdown>
            </nav>
          </header>
          <main class="main" :class="{ 'no-pad': isLogin }">
            <router-view />
          </main>
          <footer v-if="!isLogin" class="footer">
            <n-el>Docker VM · 一键开 SSH 容器</n-el>
          </footer>
        </div>
      </n-dialog-provider>
    </n-message-provider>
  </n-config-provider>
</template>

<style>
* { box-sizing: border-box; }
html, body, #app { margin: 0; padding: 0; height: 100%; }
body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'PingFang SC', 'Microsoft YaHei', sans-serif;
  background: #f5f7fa;
  color: #1f2329;
}
.layout { min-height: 100vh; display: flex; flex-direction: column; }
.header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 0 24px; height: 56px;
  background: #fff;
  border-bottom: 1px solid #e5e6eb;
  box-shadow: 0 1px 3px rgba(0,0,0,0.04);
}
.brand { font-size: 18px; font-weight: 600; }
.nav { display: flex; align-items: center; }
.nav-link {
  margin-right: 16px;
  color: #4e5969;
  text-decoration: none;
  font-size: 14px;
}
.nav-link.router-link-exact-active { color: #2080f0; font-weight: 500; }
.user-box { cursor: pointer; padding: 4px 10px; border-radius: 6px; }
.user-box:hover { background: #f2f3f5; }
.user-name { font-size: 14px; color: #1f2329; }
.main {
  flex: 1;
  width: 100%;
  max-width: 1080px;
  margin: 0 auto;
  padding: 32px 24px;
}
.main.no-pad { max-width: none; padding: 0; }
.footer {
  text-align: center; padding: 16px;
  color: #86909c; font-size: 12px;
}
</style>
