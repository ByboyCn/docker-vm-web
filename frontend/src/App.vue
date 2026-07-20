<script setup lang="ts">
import { computed, ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NMessageProvider, NDialogProvider, NConfigProvider, zhCN, dateZhCN, NEl,
  NDropdown, NSpace, NTag, NModal, NCard, NForm, NFormItem, NInput, NButton, useMessage,
} from 'naive-ui'
import { api, getStoredUser, clearStoredUser, type UserDto } from './api'

const route = useRoute()
const router = useRouter()
const msg = useMessage()

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
    { label: '修改密码', key: 'change-password' },
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
  } else if (key === 'change-password') {
    openChangePassword()
  }
}

function selectUserDropdown(key: string) {
  onUserMenu(key)
}

// ---------- 修改密码弹窗 ----------
const pwdModalShow = ref(false)
const oldPwd = ref('')
const newPwd = ref('')
const confirmPwd = ref('')
const pwdLoading = ref(false)

function openChangePassword() {
  oldPwd.value = ''
  newPwd.value = ''
  confirmPwd.value = ''
  pwdModalShow.value = true
}

async function submitChangePassword() {
  if (!oldPwd.value || !newPwd.value) {
    msg.warning('请填写完整')
    return
  }
  if (newPwd.value.length < 6) {
    msg.warning('新密码至少 6 位')
    return
  }
  if (newPwd.value !== confirmPwd.value) {
    msg.warning('两次输入的新密码不一致')
    return
  }
  pwdLoading.value = true
  try {
    await api.changePassword(oldPwd.value, newPwd.value)
    msg.success('密码修改成功,请用新密码重新登录')
    pwdModalShow.value = false
    // 改完密码强制重新登录,清掉 session
    clearStoredUser()
    user.value = null
    router.push('/login')
  } catch (e: any) {
    msg.error(e?.response?.data?.error ?? e?.message ?? '修改失败')
  } finally {
    pwdLoading.value = false
  }
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

  <!-- 修改密码弹窗 -->
  <n-modal v-model:show="pwdModalShow" :auto-focus="false">
    <n-card
      style="width: 420px; max-width: 92vw;"
      title="修改密码"
      :bordered="false"
      size="large"
      role="dialog"
      aria-modal="true"
    >
      <n-form @keyup.enter="submitChangePassword">
        <n-form-item label="旧密码">
          <n-input
            v-model:value="oldPwd"
            type="password"
            show-password-on="click"
            placeholder="请输入当前密码"
          />
        </n-form-item>
        <n-form-item label="新密码">
          <n-input
            v-model:value="newPwd"
            type="password"
            show-password-on="click"
            placeholder="至少 6 位"
          />
        </n-form-item>
        <n-form-item label="确认新密码">
          <n-input
            v-model:value="confirmPwd"
            type="password"
            show-password-on="click"
            placeholder="再次输入新密码"
            @keyup.enter="submitChangePassword"
          />
        </n-form-item>
      </n-form>
      <template #footer>
        <n-space justify="end">
          <n-button @click="pwdModalShow = false">取消</n-button>
          <n-button type="primary" :loading="pwdLoading" @click="submitChangePassword">
            确认修改
          </n-button>
        </n-space>
      </template>
    </n-card>
  </n-modal>
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
