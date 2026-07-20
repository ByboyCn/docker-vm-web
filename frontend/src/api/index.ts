import axios from 'axios'

const http = axios.create({
  baseURL: '/api',
  timeout: 30000,
})

export interface VmDto {
  key: string
  ip: string
  port: number
  username: string
  password: string
  containerName: string
  status: string
  createdAt: string
  stoppedAt: string | null
}

// ---------- key 管理(localStorage) ----------
const KEY_STORE = 'docker-vm-keys'

export function getMyKeys(): string[] {
  try {
    const raw = localStorage.getItem(KEY_STORE)
    return raw ? JSON.parse(raw) as string[] : []
  } catch {
    return []
  }
}

export function addKey(key: string) {
  const keys = getMyKeys()
  if (!keys.includes(key)) {
    keys.push(key)
    localStorage.setItem(KEY_STORE, JSON.stringify(keys))
  }
}

export function removeKey(key: string) {
  const keys = getMyKeys().filter(k => k !== key)
  localStorage.setItem(KEY_STORE, JSON.stringify(keys))
}

// ---------- admin token(sessionStorage) ----------
const ADMIN_KEY = 'docker-vm-admin-token'
export function getAdminToken(): string {
  return sessionStorage.getItem(ADMIN_KEY) ?? ''
}
export function setAdminToken(t: string) {
  sessionStorage.setItem(ADMIN_KEY, t)
}
export function clearAdminToken() {
  sessionStorage.removeItem(ADMIN_KEY)
}

// ---------- 拦截器:自动注入 header ----------
http.interceptors.request.use(cfg => {
  // 用户侧:带上所有 key
  const keys = getMyKeys()
  if (keys.length > 0) {
    cfg.headers['X-VM-Key'] = keys.join(',')
  }
  // 管理后台:带上 token
  const token = getAdminToken()
  if (token) {
    cfg.headers.Authorization = `Bearer ${token}`
  }
  return cfg
})

// ---------- API ----------
export const api = {
  health: () => http.get<{ ok: boolean }>('/health').then(r => r.data),

  createVm: () =>
    http.post<VmDto>('/vm').then(r => {
      addKey(r.data.key)
      return r.data
    }),

  listMy: () => http.get<VmDto[]>('/vm').then(r => r.data),

  getVm: (key: string) => http.get<VmDto>(`/vm/${key}`).then(r => r.data),

  destroyVm: (key: string) => http.delete(`/vm/${key}`).then(r => {
    removeKey(key)
    return r.data
  }),

  adminList: () =>
    http.get<{ total: number; running: number; items: VmDto[] }>('/admin/containers').then(r => r.data),

  adminDestroy: (key: string) => http.delete(`/admin/containers/${key}`).then(r => r.data),

  adminCleanup: () => http.post<{ ok: boolean; removed: string[] }>('/admin/cleanup-orphans').then(r => r.data),
}
