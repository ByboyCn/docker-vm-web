<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NCard, NButton, NDataTable, NTag, NSpace, NPopconfirm, NEmpty, NStatistic, useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { api, type VmDto, type UserQuotaDto } from '../api'
import ConnectionDialog from '../components/ConnectionDialog.vue'

const msg = useMessage()

const loading = ref(false)
const list = ref<VmDto[]>([])
const dialogShow = ref(false)
const currentVm = ref<VmDto | null>(null)
const quota = ref<UserQuotaDto | null>(null)

const quotaText = computed(() => {
  if (!quota.value) return ''
  const { remaining, globalTotal, globalUsed, bonus } = quota.value
  if (bonus > 0)
    return `剩余 ${remaining} 台(全局 ${Math.max(0, globalTotal - globalUsed)}/${globalTotal} + 个人加量 ${bonus})`
  return `剩余 ${remaining} 台(共 ${globalTotal} 台,已用 ${globalUsed})`
})

const hasQuota = computed(() => (quota.value?.remaining ?? 0) > 0)

async function refreshQuota() {
  try {
    quota.value = await api.getMyQuota()
  } catch (e: any) {
    // 忽略,quota 非关键
  }
}

async function refresh() {
  try {
    list.value = await api.listMy()
  } catch (e: any) {
    msg.error('加载列表失败:' + (e?.message ?? ''))
  }
}

async function create() {
  if (!hasQuota.value) {
    msg.error('今日名额已用完,请等管理员放出新名额')
    return
  }
  loading.value = true
  try {
    const vm = await api.createVm()
    currentVm.value = vm
    dialogShow.value = true
    msg.success('虚拟机创建成功')
    await Promise.all([refresh(), refreshQuota()])
  } catch (e: any) {
    const status = e?.response?.status
    const errMsg = e?.response?.data?.error ?? e?.message ?? ''
    if (status === 409) {
      msg.error(errMsg || '今日名额已用完')
      refreshQuota()
    } else {
      msg.error('创建失败:' + errMsg)
    }
  } finally {
    loading.value = false
  }
}

async function reconnect(vm: VmDto) {
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

onMounted(() => {
  refresh()
  refreshQuota()
})
</script>

<template>
  <n-space vertical :size="20">
    <n-card title="一键开机器" size="large" :bordered="false" class="hero">
      <div class="hero-body">
        <div class="hero-text">
          <h2>点击下方按钮,立刻获得一台 Docker SSH 虚拟机</h2>
          <p class="hint">基于 Alpine · 预装常用工具 · 不持久化 · 销毁即清理</p>
          <div class="quota-line" :class="{ 'no-quota': !hasQuota }">
            <span v-if="quota">{{ quotaText }}</span>
            <span v-else class="loading">加载名额...</span>
          </div>
        </div>
        <n-button
          type="primary"
          size="large"
          :loading="loading"
          :disabled="!hasQuota"
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
.quota-line {
  margin-top: 12px;
  padding: 6px 12px;
  background: rgba(255, 255, 255, 0.2);
  border-radius: 6px;
  display: inline-block;
  font-size: 14px;
  color: #fff;
  font-weight: 500;
}
.quota-line.no-quota {
  background: rgba(255, 80, 80, 0.4);
}
.quota-line.loading {
  font-weight: normal;
  opacity: 0.8;
}
.big-btn {
  font-size: 16px !important;
  height: 48px !important;
  padding: 0 28px !important;
}
</style>
