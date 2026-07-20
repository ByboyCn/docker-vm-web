import axios from 'axios'

const http = axios.create({
  baseURL: '/api',
  timeout: 30000,
  withCredentials: true,   // 带 cookie(session 鉴权)
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

export interface UserDto {
  id: string
  username: string
  isAdmin: boolean
}

export interface UserQuotaDto {
  remaining: number
  globalRemaining: number
  globalTotal: number
  globalUsed: number
  bonus: number
}

export interface AdminQuotaDto {
  total: number
  used: number
  remaining: number
  updatedAt: string
  userBonuses: Array<{
    userId: string
    username: string
    bonus: number
    note: string
    updatedAt: string
  }>
}

// ---------- 当前用户(localStorage 缓存,真正鉴权靠后端 cookie) ----------
const USER_KEY = 'docker-vm-user'

export function getStoredUser(): UserDto | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? JSON.parse(raw) as UserDto : null
  } catch {
    return null
  }
}

export function setStoredUser(u: UserDto) {
  localStorage.setItem(USER_KEY, JSON.stringify(u))
}

export function clearStoredUser() {
  localStorage.removeItem(USER_KEY)
}

export function isLoggedIn(): boolean {
  return !!getStoredUser()
}

// ---------- 401 拦截:清登录态并跳转登录页 ----------
http.interceptors.response.use(
  r => r,
  err => {
    if (err?.response?.status === 401) {
      clearStoredUser()
      // 避免在登录页本身被反复跳转
      const path = window.location.pathname
      if (path !== '/login') {
        window.location.href = '/login'
      }
    }
    return Promise.reject(err)
  }
)

// ---------- API ----------
export const api = {
  health: () => http.get<{ ok: boolean }>('/health').then(r => r.data),

  // ---------- auth ----------
  register: (username: string, password: string) =>
    http.post<UserDto>('/auth/register', { username, password }).then(r => {
      setStoredUser(r.data)
      return r.data
    }),

  login: (username: string, password: string) =>
    http.post<UserDto>('/auth/login', { username, password }).then(r => {
      setStoredUser(r.data)
      return r.data
    }),

  logout: () => http.post('/auth/logout').then(r => {
    clearStoredUser()
    return r.data
  }),

  me: () => http.get<UserDto>('/auth/me').then(r => {
    setStoredUser(r.data)
    return r.data
  }),

  changePassword: (oldPassword: string, newPassword: string) =>
    http.post('/auth/change-password', { oldPassword, newPassword }).then(r => r.data),

  // ---------- vm ----------
  createVm: () => http.post<VmDto>('/vm').then(r => r.data),

  listMy: () => http.get<VmDto[]>('/vm').then(r => r.data),

  getVm: (key: string) => http.get<VmDto>(`/vm/${key}`).then(r => r.data),

  destroyVm: (key: string) => http.delete(`/vm/${key}`).then(r => r.data),

  // ---------- admin ----------
  adminList: () =>
    http.get<{ total: number; running: number; items: VmDto[] }>('/admin/containers').then(r => r.data),

  adminDestroy: (key: string) => http.delete(`/admin/containers/${key}`).then(r => r.data),

  adminCleanup: () => http.post<{ ok: boolean; removed: string[] }>('/admin/cleanup-orphans').then(r => r.data),

  adminUsers: () =>
    http.get<{ items: Array<{ id: string; username: string; isAdmin: boolean; createdAt: string; containerCount: number; bonus: number }> }>('/admin/users').then(r => r.data),

  // ---------- quota ----------
  getMyQuota: () => http.get<UserQuotaDto>('/quota').then(r => r.data),

  adminGetQuota: () => http.get<AdminQuotaDto>('/admin/quota').then(r => r.data),

  adminSetQuota: (total: number, used?: number) =>
    http.put('/admin/quota', { total, used }).then(r => r.data),

  adminResetQuota: (total: number) =>
    http.post('/admin/quota/reset', { total }).then(r => r.data),

  adminSetUserBonus: (userId: string, bonus: number, note = '') =>
    http.post(`/admin/quota/users/${userId}/bonus`, { bonus, note }).then(r => r.data),

  // ---------- disk usage ----------
  adminDiskUsage: () =>
    http.get<{
      threshold: number
      thresholdHuman: string
      items: Array<{
        key: string
        containerName: string
        username: string
        status: string
        diskUsageBytes: number
        diskUsageHuman: string
        overLimit: boolean
      }>
    }>('/admin/disk-usage').then(r => r.data),
}

// 把 axios 实例也导出,供拦截器外使用
export { http }
