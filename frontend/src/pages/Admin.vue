<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import {
  NCard, NButton, NDataTable, NTag, NSpace, NPopconfirm, NEmpty, NStatistic,
  NGrid, NGi, NTabs, NTabPane, useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { api, type VmDto } from '../api'

const msg = useMessage()

const tab = ref<'containers' | 'users'>('containers')

const list = ref<VmDto[]>([])
const total = ref(0)
const running = ref(0)
const loading = ref(false)

const users = ref<Array<{ Id: string; Username: string; IsAdmin: boolean; CreatedAt: string; containerCount: number }>>([])

async function refresh() {
  loading.value = true
  try {
    const [c, u] = await Promise.all([api.adminList(), api.adminUsers()])
    list.value = c.items
    total.value = c.total
    running.value = c.running
    users.value = u.items
  } catch (e: any) {
    if (e?.response?.status === 403) {
      msg.error('只有管理员可以访问后台')
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

const containerColumns: DataTableColumns<VmDto> = [
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

const userColumns = [
  { title: '用户名', key: 'Username' },
  {
    title: '角色',
    key: 'IsAdmin',
    render: (r: any) =>
      r.IsAdmin
        ? h(NTag, { type: 'warning', size: 'small', round: true }, () => 'admin')
        : h(NTag, { size: 'small', round: true }, () => 'user'),
  },
  { title: '容器数', key: 'containerCount' },
  { title: '注册时间', key: 'CreatedAt', render: (r: any) => fmtTime(r.CreatedAt) },
]

onMounted(refresh)
</script>

<template>
  <n-space vertical :size="20">
    <n-card title="管理后台" size="large" :bordered="false">
      <template #header-extra>
        <n-space>
          <n-button size="small" @click="refresh" :loading="loading">刷新</n-button>
          <n-button size="small" @click="cleanupOrphans">清理孤儿</n-button>
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

    <n-card size="large" :bordered="false">
      <n-tabs v-model:value="tab" type="line" animated>
        <n-tab-pane name="containers" tab="所有容器">
          <n-data-table
            v-if="list.length > 0"
            :columns="containerColumns"
            :data="list"
            :bordered="false"
            :pagination="{ pageSize: 20 }"
          />
          <n-empty v-else description="暂无容器" style="padding: 40px 0;" />
        </n-tab-pane>

        <n-tab-pane name="users" tab="用户">
          <n-data-table
            v-if="users.length > 0"
            :columns="userColumns"
            :data="users"
            :bordered="false"
            :pagination="false"
          />
          <n-empty v-else description="暂无用户" style="padding: 40px 0;" />
        </n-tab-pane>
      </n-tabs>
    </n-card>
  </n-space>
</template>
