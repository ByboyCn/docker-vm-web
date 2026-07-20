<script setup lang="ts">
import { h, onMounted, ref } from 'vue'
import {
  NCard, NButton, NDataTable, NTag, NSpace, NPopconfirm, NEmpty, useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { api, type VmDto } from '../api'
import ConnectionDialog from '../components/ConnectionDialog.vue'

const msg = useMessage()

const loading = ref(false)
const list = ref<VmDto[]>([])
const dialogShow = ref(false)
const currentVm = ref<VmDto | null>(null)

async function refresh() {
  try {
    list.value = await api.listMy()
  } catch (e: any) {
    msg.error('加载列表失败:' + (e?.message ?? ''))
  }
}

async function create() {
  loading.value = true
  try {
    const vm = await api.createVm()
    currentVm.value = vm
    dialogShow.value = true
    msg.success('虚拟机创建成功')
    await refresh()
  } catch (e: any) {
    msg.error('创建失败:' + (e?.response?.data?.error ?? e?.message ?? ''))
  } finally {
    loading.value = false
  }
}

async function reconnect(vm: VmDto) {
  // 刷新一下最新状态再弹
  try {
    currentVm.value = await api.getVm(vm.key)
  } catch {
    currentVm.value = vm
  }
  dialogShow.value = true
}

async function destroy(vm: VmDto) {
  try {
    await api.destroyVm(vm.key)
    msg.success('已销毁')
    await refresh()
  } catch (e: any) {
    msg.error('销毁失败:' + (e?.response?.data?.error ?? e?.message ?? ''))
    await refresh()
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
      h('div', { style: 'display:flex;gap:8px;' }, [
        h(NButton, { size: 'small', type: 'primary', ghost: true, onClick: () => reconnect(r) }, () => '查看连接'),
        h(
          NPopconfirm,
          { onPositiveClick: () => destroy(r) },
          {
            trigger: () => h(NButton, { size: 'small', type: 'error', ghost: true }, () => '销毁'),
            default: () => '确定销毁这台虚拟机吗?此操作不可撤销。',
          }
        ),
      ]),
  },
]

onMounted(refresh)
</script>

<template>
  <n-space vertical :size="20">
    <n-card title="一键开机器" size="large" :bordered="false" class="hero">
      <div class="hero-body">
        <div class="hero-text">
          <h2>点击下方按钮,立刻获得一台 Docker SSH 虚拟机</h2>
          <p class="hint">基于 Alpine · 预装常用工具 · 不持久化 · 销毁即清理</p>
        </div>
        <n-button
          type="primary"
          size="large"
          :loading="loading"
          @click="create"
          class="big-btn"
        >
          🚀 一键开机器
        </n-button>
      </div>
    </n-card>

    <n-card title="我的容器" size="large" :bordered="false">
      <template #header-extra>
        <n-button size="small" quaternary @click="refresh">刷新</n-button>
      </template>
      <n-data-table
        v-if="list.length > 0"
        :columns="columns"
        :data="list"
        :bordered="false"
        :pagination="false"
      />
      <n-empty v-else description="还没有任何容器,点上面按钮开一台吧" style="padding: 40px 0;" />
    </n-card>
  </n-space>

  <ConnectionDialog v-model:show="dialogShow" :vm="currentVm" />
</template>

<style scoped>
.hero {
  background: linear-gradient(135deg, #2080f0 0%, #18a058 100%);
  color: #fff;
}
.hero :deep(.n-card-header__title) { color: #fff; }
.hero-body {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 24px;
  flex-wrap: wrap;
}
.hero-text h2 { margin: 0 0 8px; font-size: 20px; color: #fff; }
.hint { margin: 0; color: rgba(255,255,255,0.85); font-size: 13px; }
.big-btn {
  font-size: 16px !important;
  height: 48px !important;
  padding: 0 28px !important;
}
</style>
