<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { NCard, NTabs, NTabPane, NForm, NFormItem, NInput, NButton, NSpace, useMessage } from 'naive-ui'
import { api } from '../api'

const router = useRouter()
const msg = useMessage()

const tab = ref<'login' | 'register'>('login')

const username = ref('')
const password = ref('')
const loading = ref(false)

async function submit() {
  if (!username.value || !password.value) {
    msg.warning('请填写用户名和密码')
    return
  }
  loading.value = true
  try {
    if (tab.value === 'login') {
      const u = await api.login(username.value, password.value)
      msg.success(`欢迎回来,${u.username}`)
    } else {
      const u = await api.register(username.value, password.value)
      msg.success(`注册成功,欢迎 ${u.username}`)
    }
    router.push('/')
  } catch (e: any) {
    msg.error(e?.response?.data?.error ?? e?.message ?? '操作失败')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-wrap">
    <n-card class="login-card" :bordered="false" size="large">
      <div class="brand">
        <span class="logo">🐳</span>
        <h2>Docker 虚拟机</h2>
      </div>

      <n-tabs v-model:value="tab" type="line" animated size="large">
        <n-tab-pane name="login" tab="登录">
          <n-form @keyup.enter="submit">
            <n-form-item label="用户名">
              <n-input v-model:value="username" placeholder="请输入用户名" :input-props="{ autocomplete: 'username' }" />
            </n-form-item>
            <n-form-item label="密码">
              <n-input
                v-model:value="password"
                type="password"
                show-password-on="click"
                placeholder="请输入密码"
                :input-props="{ autocomplete: 'current-password' }"
              />
            </n-form-item>
            <n-space vertical :size="12">
              <n-button type="primary" block :loading="loading" @click="submit">登录</n-button>
            </n-space>
          </n-form>
        </n-tab-pane>

        <n-tab-pane name="register" tab="注册">
          <n-form @keyup.enter="submit">
            <n-form-item label="用户名">
              <n-input v-model:value="username" placeholder="3-32 字符,字母数字 _ - . @" />
            </n-form-item>
            <n-form-item label="密码">
              <n-input
                v-model:value="password"
                type="password"
                show-password-on="click"
                placeholder="至少 6 位"
              />
            </n-form-item>
            <n-space vertical :size="12">
              <n-button type="primary" block :loading="loading" @click="submit">注册并登录</n-button>
            </n-space>
          </n-form>
        </n-tab-pane>
      </n-tabs>
    </n-card>
  </div>
</template>

<style scoped>
.login-wrap {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #2080f0 0%, #18a058 100%);
  padding: 20px;
}
.login-card {
  width: 380px;
  max-width: 92vw;
  border-radius: 12px;
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.15);
}
.brand {
  text-align: center;
  margin-bottom: 8px;
}
.logo {
  font-size: 40px;
}
.brand h2 {
  margin: 4px 0 0;
  color: #1f2329;
}
</style>
