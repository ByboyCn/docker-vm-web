<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import {
  NCard, NButton, NDataTable, NTag, NSpace, NPopconfirm, NEmpty, NStatistic,
  NGrid, NGi, NTabs, NTabPane, NInput, NInputGroup, NInputNumber, NModal, NForm,
  NFormItem, useMessage,
} from 'naive-ui'
import type { DataTableColumns } from 'naive-ui'
import { api, type VmDto, type AdminQuotaDto } from '../api'

const msg = useMessage()

const tab = ref<'containers' | 'users' | 'quota' | 'disk'>('quota')

// ---------- 容器 ----------
const list = ref<VmDto[]>([])
const total = ref(0)
const running = ref(0)
const loading = ref(false)

// ---------- 磁盘占用 ----------
interface DiskUsageItem {
  key: string
  containerName: string
  username: string
  status: string
  diskUsageBytes: number
  diskUsageHuman: string
  overLimit: boolean
}
const diskUsage = ref<DiskUsageItem[]>([])
const diskThresholdHuman = ref('')

// ---------- 用户 ----------
const users = ref<Array<{ id: string; username: string; isAdmin: boolean; createdAt: string; containerCount: number; bonus: number }>>([])

// ---------- 名额 ----------
const quota = ref<AdminQuotaDto | null>(null)
const editTotal = ref<number>(5)
const resetTotal = ref<number>(5)

// 编辑 bonus 的弹窗
const bonusModalShow = ref(false)
const editingUser = ref<{ id: string; name: string } | null>(null)
const editBonusValue = ref<number>(0)
const editBonusNote = ref<string>('')

async function refresh() {
  loading.value = true
  try {
    const [c, u, q, d] = await Promise.all([
      api.adminList(), api.adminUsers(), api.adminGetQuota(), api.adminDiskUsage(),
    ])
    list.value = c.items
    total.value = c.total
    running.value = c.running
    users.value = u.items
    quota.value = q
    editTotal.value = q.total
    resetTotal.value = q.total
    diskUsage.value = d.items
    diskThresholdHuman.value = d.thresholdHuman
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

// ---------- 名额操作 ----------
async function saveTotal() {
  try {
    await api.adminSetQuota(editTotal.value)
    msg.success(`总额度已设为 ${editTotal.value}`)
    await refresh()
  } catch (e: any) {
    msg.error('设置失败:' + (e?.response?.data?.error ?? e?.message ?? ''))
  }
}

async function resetQuota() {
  try {
    await api.adminResetQuota(resetTotal.value)
    msg.success(`名额已重置:总额 ${resetTotal.value},已用清零`)
    await refresh()
  } catch (e: any) {
    msg.error('重置失败:' + (e?.response?.data?.error ?? e?.message ?? ''))
  }
}

function openBonusModal(row: { id: string; username: string; bonus: number }) {
  editingUser.value = { id: row.id, name: row.username }
  editBonusValue.value = row.bonus
  // 找原有备注
  const item = quota.value?.userBonuses.find(u => u.userId === row.id)
  editBonusNote.value = item?.note ?? ''
  bonusModalShow.value = true
}

async function saveBonus() {
  if (!editingUser.value) return
  try {
    await api.adminSetUserBonus(editingUser.value.id, editBonusValue.value, editBonusNote.value)
    msg.success(`${editingUser.value.name} 的加量已设为 ${editBonusValue.value}`)
    bonusModalShow.value = false
    await refresh()
  } catch (e: any) {
    msg.error('设置失败:' + (e?.response?.data?.error ?? e?.message ?? ''))
  }
}

// ---------- 工具 ----------
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

const userColumns = computed<DataTableColumns<any>>(() => [
  { title: '用户名', key: 'username' },
  {
    title: '角色',
    key: 'isAdmin',
    render: (r: any) =>
      r.isAdmin
        ? h(NTag, { type: 'warning', size: 'small', round: true }, () => 'admin')
        : h(NTag, { size: 'small', round: true }, () => 'user'),
  },
  { title: '容器数', key: 'containerCount' },
  {
    title: '个人加量',
    key: 'bonus',
    render: (r: any) =>
      h(NButton, { size: 'small', tertiary: true, onClick: () => openBonusModal(r) },
        () => r.bonus > 0 ? `${r.bonus} 台 (点击编辑)` : '0 (点击设置)'),
  },
  { title: '注册时间', key: 'createdAt', render: (r: any) => fmtTime(r.createdAt) },
])

const diskColumns = computed<DataTableColumns<DiskUsageItem>>(() => [
  { title: '容器', key: 'containerName', ellipsis: { tooltip: true } },
  { title: '用户', key: 'username' },
  { title: '状态', key: 'status' },
  {
    title: '磁盘占用',
    key: 'diskUsageBytes',
    render: r =>
      h(
        NTag,
        {
          type: r.overLimit ? 'error' : (r.diskUsageBytes > 1024 * 1024 * 1024 ? 'warning' : 'success'),
          size: 'small',
          round: true,
        },
        () => r.diskUsageHuman,
      ),
  },
  {
    title: '操作',
    key: 'actions',
    render: r =>
      h(
        NPopconfirm,
        { onPositiveClick: () => destroyDisk(r.key) },
        {
          trigger: () => h(NButton, { size: 'small', type: 'error', ghost: true }, () => '强制销毁'),
          default: () => '确定强制销毁该容器并清理磁盘?',
        }
      ),
  },
])

async function destroyDisk(key: string) {
  try {
    await api.adminDestroy(key)
    msg.success('已销毁')
    await refresh()
  } catch (e: any) {
    msg.error('销毁失败:' + (e?.message ?? ''))
  }
}

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

      <n-grid :cols="3" :x-gap="16">
        <n-gi>
          <n-statistic label="容器总数" :value="total" />
        </n-gi>
        <n-gi>
          <n-statistic label="运行中" :value="running" />
        </n-gi>
        <n-gi>
          <n-statistic label="剩余名额" :value="quota?.remaining ?? 0" />
        </n-gi>
      </n-grid>
    </n-card>

    <n-card size="large" :bordered="false">
      <n-tabs v-model:value="tab" type="line" animated>
        <!-- 名额管理 -->
        <n-tab-pane name="quota" tab="名额管理">
          <div v-if="quota" class="quota-panel">
            <n-card title="全局名额池" size="medium" :bordered="true" class="sub-card">
              <n-space align="center" :size="24">
                <n-statistic label="总额度" :value="quota.total" />
                <n-statistic label="已消耗" :value="quota.used" />
                <n-statistic label="剩余" :value="quota.remaining" />
              </n-space>
              <p class="tip">说明:用户开机器时优先消耗全局池,池空了再消耗"个人加量"。销毁不退名额。</p>

              <n-space align="center" :size="12" style="margin-top: 16px;">
                <n-input-group>
                  <n-input-number v-model:value="editTotal" :min="0" :max="10000" />
                  <n-button type="primary" @click="saveTotal">修改总额度</n-button>
                </n-input-group>
                <span class="or">或</span>
                <n-input-group>
                  <n-input-number v-model:value="resetTotal" :min="0" :max="10000" />
                  <n-popconfirm @positive-click="resetQuota">
                    <template #trigger>
                      <n-button type="warning">一键重置(已用清零)</n-button>
                    </template>
                    确定重置吗?所有"已消耗"会归零,总额度变为 {{ resetTotal }}。
                  </n-popconfirm>
                </n-input-group>
              </n-space>
            </n-card>

            <n-card title="已发放个人加量" size="medium" :bordered="true" class="sub-card">
              <n-data-table
                v-if="quota.userBonuses.length > 0"
                :columns="[
                  { title: '用户名', key: 'username' },
                  { title: '加量数', key: 'bonus' },
                  { title: '备注', key: 'note' },
                  { title: '更新时间', key: 'updatedAt', render: (r: any) => fmtTime(r.updatedAt) },
                ]"
                :data="quota.userBonuses"
                :bordered="false"
                :pagination="false"
              />
              <n-empty v-else description="还没有给任何用户发放额外加量" style="padding: 24px 0;" />
              <p class="tip">在下方"用户"Tab 里可以为每个用户单独设置加量。</p>
            </n-card>
          </div>
        </n-tab-pane>

        <!-- 所有容器 -->
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

        <!-- 用户 -->
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

        <n-tab-pane name="disk" :tab="`磁盘占用${diskUsage.some(d => d.overLimit) ? ' ⚠️' : ''}`">
          <p class="tip" style="margin-bottom: 12px;">
            后台每 10 分钟扫描一次,显示容器可写层总大小。
            超过 <b>{{ diskThresholdHuman }}</b> 标红。/home 的 5G loop 文件不计入这里(已硬限)。
          </p>
          <n-data-table
            v-if="diskUsage.length > 0"
            :columns="diskColumns"
            :data="diskUsage"
            :bordered="false"
            :pagination="false"
            :row-key="(r: DiskUsageItem) => r.key"
          />
          <n-empty v-else description="暂无容器" style="padding: 40px 0;" />
        </n-tab-pane>
      </n-tabs>
    </n-card>
  </n-space>

  <!-- 编辑 bonus 弹窗 -->
  <n-modal
    v-model:show="bonusModalShow"
    :auto-focus="false"
  >
    <n-card
      style="width: 420px; max-width: 92vw;"
      :title="`设置 ${editingUser?.name} 的个人加量`"
      :bordered="false"
      size="large"
      role="dialog"
      aria-modal="true"
    >
      <n-form>
        <n-form-item label="加量数量">
          <n-input-number v-model:value="editBonusValue" :min="0" :max="100" style="width: 100%;" />
        </n-form-item>
        <n-form-item label="备注(可选)">
          <n-input v-model:value="editBonusNote" placeholder="例如:朋友、特殊申请" />
        </n-form-item>
      </n-form>
      <p class="tip">个人加量只对该用户有效。全局池耗尽时,该用户仍可用个人加量开机器。</p>
      <template #footer>
        <n-space justify="end">
          <n-button @click="bonusModalShow = false">取消</n-button>
          <n-button type="primary" @click="saveBonus">保存</n-button>
        </n-space>
      </template>
    </n-card>
  </n-modal>
</template>

<style scoped>
.quota-panel { display: flex; flex-direction: column; gap: 16px; }
.sub-card { background: #fafbfc; }
.tip { margin: 8px 0 0; font-size: 12px; color: #86909c; line-height: 1.6; }
.or { color: #86909c; font-size: 13px; }
</style>
