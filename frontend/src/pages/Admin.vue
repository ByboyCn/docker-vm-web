<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import {
  NCard, NButton, NDataTable, NTag, NSpace, NPopconfirm, NEmpty, NStatistic,
  NGrid, NGi, NModal, NInput, NInputGroup, useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import {
  api, getAdminToken, setAdminToken, clearAdminToken, type VmDto,
} from '../api'

const msg = useMessage()

const tokenInput = ref('')
const tokenModalShow = ref(false)
const list = ref<VmDto[]>([])
const total = ref(0)
const running = ref(0)
const loading = ref(false)

const authorized = ref(!!getAdminToken())

function promptToken() {
  tokenInput.value = getAdminToken()
  tokenModalShow.value = true
}

function saveToken() {
  const t = tokenInput.value.trim()
  if (!t) {
    msg.warning('请输入 token')
    return
  }
  setAdminToken(t)
  authorized.value = true
  tokenModalShow.value = false
  refresh()
}

function logout() {
  clearAdminToken()
  authorized.value = false
  list.value = []
  total.value = 0
  running.value = 0
}

async function refresh() {
  if (!authorized.value) {
    promptToken()
    return
  }
  loading.value = true
  try {
    const data = await api.adminList()
    list.value = data.items
    total.value = data.total
    running.value = data.running
  } catch (e: any) {
    if (e?.response?.status === 401) {
      msg.error('Token 无效或已过期,请重新输入')
      authorized.value = false
      promptToken()
    } else {
      msg.error('加载失败:' + (e?.message ?? ''))
    }
  } finally {
    loading.value = false
  }
}

async function destroy(vm: VmDto) {
  try {
    await api.adminDestroy(vm.key)
    msg.success('已强制销毁')
    await refresh()
  } catch (e: any) {
    msg.error('销毁失败:' + (e?.message ?? ''))
  }
}

async function cleanupOrphans() {
  try {
    const r = await api.adminCleanup()
    msg.success(`已清理 ${r.removed.length} 个孤儿记录`)
    await refresh()
  } catch (e: any) {
    msg.error('清理失败:' + (e?.message ?? ''))
  }
}

function statusType(s: string): 'success' | 'warning' | 'error' | 'default' {
  if (s === 'running') return 'success'
  if (s === 'exited') return 'warning'
  if (s === 'missing' || s === 'dead') return 'error'
  return 'default'
}

function fmtTime(t: string | null): string {
  if (!t) return '-'
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

const columns: DataTableColumns<VmDto> = [
  { title: '容器', key: 'containerName', ellipsis: { tooltip: true } },
  { title: 'Key', key: 'key', ellipsis: { tooltip: true } },
  {
    title: '地址',
    key: 'addr',
    render: r => `${r.ip}:${r.port}`,
  },
  { title: '用户名', key: 'username' },
  {
    title: '状态',
    key: 'status',
    render: r => h(NTag, { type: statusType(r.status), size: 'small', round: true }, () => r.status),
  },
  { title: '创建时间', key: 'createdAt', render: r => fmtTime(r.createdAt) },
  {
    title: '操作',
    key: 'actions',
    render: r =>
      h(
        NPopconfirm,
        { onPositiveClick: () => destroy(r) },
        {
          trigger: () => h(NButton, { size: 'small', type: 'error', ghost: true }, () => '强制销毁'),
          default: () => '确定强制销毁该容器?数据库记录也会被删除。',
        }
      ),
  },
]

onMounted(() => {
  if (authorized.value) refresh()
  else promptToken()
})
</script>

<template>
  <n-space vertical :size="20">
    <n-card title="管理后台" size="large" :bordered="false">
      <template #header-extra>
        <n-space>
          <n-button size="small" @click="refresh" :loading="loading">刷新</n-button>
          <n-button size="small" @click="cleanupOrphans">清理孤儿</n-button>
          <n-button size="small" quaternary type="error" @click="logout">退出</n-button>
        </n-space>
      </template>

      <n-grid :cols="2" :x-gap="16">
        <n-gi>
          <n-statistic label="容器总数" :value="total" />
        </n-gi>
        <n-gi>
          <n-statistic label="运行中" :value="running" />
        </n-gi>
      </n-grid>
    </n-card>

    <n-card title="所有容器" size="large" :bordered="false">
      <n-data-table
        v-if="list.length > 0"
        :columns="columns"
        :data="list"
        :bordered="false"
        :pagination="{ pageSize: 20 }"
      />
      <n-empty v-else description="暂无容器" style="padding: 40px 0;" />
    </n-card>
  </n-space>

  <n-modal
    :show="tokenModalShow"
    @update:show="tokenModalShow = $event"
    :mask-closable="false"
    :auto-focus="false"
  >
    <n-card
      style="width: 420px; max-width: 92vw;"
      title="管理后台认证"
      :bordered="false"
      size="large"
      role="dialog"
      aria-modal="true"
    >
      <n-input-group>
        <n-input
          v-model:value="tokenInput"
          placeholder="请输入 ADMIN_TOKEN"
          type="password"
          show-password-on="click"
          @keyup.enter="saveToken"
        />
        <n-button type="primary" @click="saveToken">确认</n-button>
      </n-input-group>
      <p class="tip">Token 在后端的 .env 中配置(ADMIN_TOKEN)。</p>
    </n-card>
  </n-modal>
</template>

<style scoped>
.tip {
  margin: 12px 0 0;
  font-size: 12px;
  color: #86909c;
}
</style>
