<script setup lang="ts">
import { computed } from 'vue'
import { NModal, NCard, NSpace, NButton, NInputGroup, NInput, NTag, useMessage } from 'naive-ui'
import type { VmDto } from '../api'

const props = defineProps<{ show: boolean; vm: VmDto | null }>()
const emit = defineEmits<{ (e: 'update:show', v: boolean): void }>()

const msg = useMessage()

const sshCommand = computed(() =>
  props.vm ? `ssh ${props.vm.username}@${props.vm.ip} -p ${props.vm.port}` : ''
)

async function copy(text: string, label = '已复制') {
  try {
    await navigator.clipboard.writeText(text)
    msg.success(`${label}:${text}`)
  } catch {
    // 兜底
    const ta = document.createElement('textarea')
    ta.value = text
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    msg.success(`${label}:${text}`)
  }
}
</script>

<template>
  <n-modal
    :show="show"
    @update:show="emit('update:show', $event)"
    :mask-closable="true"
    :auto-focus="false"
  >
    <n-card
      style="width: 560px; max-width: 92vw;"
      title="🎉 虚拟机已就绪"
      :bordered="false"
      size="huge"
      role="dialog"
      aria-modal="true"
    >
      <template #header-extra>
        <n-tag :type="vm?.status === 'running' ? 'success' : 'warning'" size="small" round>
          {{ vm?.status }}
        </n-tag>
      </template>

      <n-space vertical :size="14" v-if="vm">
        <div class="tip">
          ⚠️ 请保存下面的连接信息。当前浏览器会记住这台机器,但请把 IP/端口/密码另行记下来以防丢失。
        </div>

        <n-input-group>
          <n-input :value="vm.ip" readonly style="flex: 2;">
            <template #prefix><span class="lbl">IP</span></template>
          </n-input>
          <n-button type="primary" ghost @click="copy(vm.ip, 'IP')">复制</n-button>
        </n-input-group>

        <n-input-group>
          <n-input :value="String(vm.port)" readonly style="flex: 2;">
            <template #prefix><span class="lbl">端口</span></template>
          </n-input>
          <n-button type="primary" ghost @click="copy(String(vm.port), '端口')">复制</n-button>
        </n-input-group>

        <n-input-group>
          <n-input :value="vm.username" readonly style="flex: 2;">
            <template #prefix><span class="lbl">用户名</span></template>
          </n-input>
          <n-button type="primary" ghost @click="copy(vm.username, '用户名')">复制</n-button>
        </n-input-group>

        <n-input-group>
          <n-input :value="vm.password" readonly style="flex: 2;" type="password" show-password-on="click">
            <template #prefix><span class="lbl">密码</span></template>
          </n-input>
          <n-button type="primary" ghost @click="copy(vm.password, '密码')">复制</n-button>
        </n-input-group>

        <div class="ssh-line">
          <div class="lbl">SSH 命令</div>
          <code class="ssh-cmd" @click="copy(sshCommand, 'SSH 命令')" title="点击复制">
            {{ sshCommand }}
          </code>
        </div>

        <div class="actions">
          <n-button type="primary" @click="copy(sshCommand, 'SSH 命令')">复制 SSH 命令</n-button>
          <n-button @click="emit('update:show', false)">我已记住</n-button>
        </div>
      </n-space>
    </n-card>
  </n-modal>
</template>

<style scoped>
.tip {
  background: #fff7e6;
  border: 1px solid #ffd591;
  color: #ad6800;
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 13px;
  line-height: 1.5;
}
.lbl {
  color: #86909c;
  font-size: 12px;
  width: 56px;
  display: inline-block;
}
.ssh-line { margin-top: 4px; }
.ssh-line .lbl { margin-bottom: 6px; display: block; }
.ssh-cmd {
  display: block;
  background: #2b2b2b;
  color: #f5f5f5;
  padding: 10px 12px;
  border-radius: 6px;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  cursor: pointer;
  user-select: all;
  word-break: break-all;
}
.ssh-cmd:hover { background: #1a1a1a; }
.actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  margin-top: 8px;
}
</style>
