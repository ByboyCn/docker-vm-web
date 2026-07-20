<script setup lang="ts">
import { NMessageProvider, NDialogProvider, NConfigProvider, zhCN, dateZhCN, NEl } from 'naive-ui'
</script>

<template>
  <n-config-provider :locale="zhCN" :date-locale="dateZhCN">
    <n-message-provider>
      <n-dialog-provider>
        <div class="layout">
          <header class="header">
            <div class="brand">🐳 Docker 虚拟机</div>
            <nav>
              <router-link to="/" class="nav-link">首页</router-link>
              <router-link to="/admin" class="nav-link">管理后台</router-link>
            </nav>
          </header>
          <main class="main">
            <router-view />
          </main>
          <footer class="footer">
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
.layout {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}
.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  height: 56px;
  background: #fff;
  border-bottom: 1px solid #e5e6eb;
  box-shadow: 0 1px 3px rgba(0,0,0,0.04);
}
.brand { font-size: 18px; font-weight: 600; }
.nav-link {
  margin-left: 16px;
  color: #4e5969;
  text-decoration: none;
  font-size: 14px;
}
.nav-link.router-link-exact-active { color: #2080f0; font-weight: 500; }
.main {
  flex: 1;
  width: 100%;
  max-width: 1080px;
  margin: 0 auto;
  padding: 32px 24px;
}
.footer {
  text-align: center;
  padding: 16px;
  color: #86909c;
  font-size: 12px;
}
</style>
