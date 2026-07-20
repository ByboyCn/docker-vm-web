#!/bin/sh
set -e

USER="${SSH_USER:-user}"
PASSWORD="${SSH_PASSWORD:-changeme}"

# 创建用户(若已存在则跳过)
if ! id -u "$USER" >/dev/null 2>&1; then
    adduser -D -s /bin/bash "$USER"
fi
echo "$USER:$PASSWORD" | chpasswd

# 允许该用户使用 sudo(无密码),方便用户在虚拟机里装东西
echo "$USER ALL=(ALL) NOPASSWD: ALL" > /etc/sudoers.d/$USER
chmod 0440 /etc/sudoers.d/$USER

# (重新)生成 host key,避免多容器共用一份
rm -f /etc/ssh/ssh_host_*
ssh-keygen -A

# ---------- 安装 free/nproc/lscpu 的"包装脚本" ----------
# 这些命令默认走 syscall 或 /sys,会看到宿主真实资源。改为读:
# - CPU:优先读 backend 透传的 VM_CPU_CORES 环境变量(cgroup 兜底)
# - 内存:读 LXCFS 渲染过的 /proc/meminfo
install_wrappers() {
    # fake nproc:优先环境变量,cgroup v2/v1 兜底
    cat > /usr/local/bin/nproc <<'NPROC_EOF'
#!/bin/sh
read_cpu_count() {
    # 1. 优先:backend 创建容器时透传的环境变量(最可靠)
    if [ -n "$VM_CPU_CORES" ]; then
        # 可能是小数,如 0.5,向上取整(0.5 核也算 "1")
        n=$(awk -v v="$VM_CPU_CORES" 'BEGIN {
            i = int(v)
            if (v > i) i++
            if (i < 1) i = 1
            print i
        }')
        echo "$n" && return
    fi
    # 2. cgroup v2:/sys/fs/cgroup/cpu.max 格式 "quota period" 或 "max"
    if [ -r /sys/fs/cgroup/cpu.max ]; then
        read -r quota period < /sys/fs/cgroup/cpu.max 2>/dev/null
        if [ "$quota" != "max" ] && [ -n "$quota" ] && [ -n "$period" ] && [ "$period" != "0" ]; then
            n=$(( quota / period ))
            [ "$n" -ge 1 ] && echo "$n" && return
        fi
    fi
    # 3. cgroup v1:cpu.cfs_quota_us / cpu.cfs_period_us
    if [ -r /sys/fs/cgroup/cpu/cpu.cfs_quota_us ] && [ -r /sys/fs/cgroup/cpu/cpu.cfs_period_us ]; then
        quota=$(cat /sys/fs/cgroup/cpu/cpu.cfs_quota_us 2>/dev/null)
        period=$(cat /sys/fs/cgroup/cpu/cpu.cfs_period_us 2>/dev/null)
        if [ "$quota" -gt 0 ] && [ "$period" -gt 0 ] 2>/dev/null; then
            n=$(( quota / period ))
            [ "$n" -ge 1 ] && echo "$n" && return
        fi
    fi
    # 4. 兜底:数 cpuinfo(可能不准,但好过啥都没有)
    grep -c '^processor' /proc/cpuinfo 2>/dev/null || echo 1
}
read_cpu_count
NPROC_EOF
    chmod +x /usr/local/bin/nproc

    # fake free:从 /proc/meminfo 提取
    cat > /usr/local/bin/free <<'FREE_EOF'
#!/bin/sh
# 读 LXCFS 渲染过的 /proc/meminfo,而不是 cgroup/syscall
awk -v unit="${1:--m}" '
BEGIN {
    factor = (unit == "-k") ? 1 : (unit == "-m") ? 1024 : (unit == "-g") ? 1048576 : 1024
    printf "%-16s %10s %10s %10s %10s %10s\n", "", "total", "used", "free", "shared", "buff/cache"
}
/^MemTotal:/      { total = $2 }
/^MemFree:/       { free = $2 }
/^MemAvailable:/  { avail = $2 }
/^Cached:/        { cached = $2 }
/^Buffers:/       { buf = $2 }
/^Shmem:/         { shmem = $2 }
/^SwapTotal:/     { sw_total = $2 }
/^SwapFree:/      { sw_free = $2 }
END {
    used = total - free - buf - cached + shmem
    if (used < 0) used = 0
    bc = buf + cached
    printf "%-16s %10d %10d %10d %10d %10d\n", "Mem:", total/factor, used/factor, free/factor, shmem/factor, bc/factor
    printf "%-16s %10d %10d %10d\n", "Swap:", sw_total/factor, (sw_total-sw_free)/factor, sw_free/factor
}' /proc/meminfo
FREE_EOF
    chmod +x /usr/local/bin/free

    # fake lscpu:简化版,核数走 nproc,型号走 cpuinfo 第一行
    cat > /usr/local/bin/lscpu <<'LSCPU_EOF'
#!/bin/sh
n=$(/usr/local/bin/nproc)
echo "Architecture:        $(uname -m)"
echo "CPU(s):              $n"
grep -m1 '^model name' /proc/cpuinfo 2>/dev/null | sed 's/^model name[[:space:]]*: */Model name:          /'
LSCPU_EOF
    chmod +x /usr/local/bin/lscpu
}
install_wrappers

# 启动 sshd(前台守护 → 后台跑,容器用 tail 续命)
/usr/sbin/sshd

echo "================================================"
echo " SSH 虚拟机已就绪"
echo " 用户名: $USER"
echo " 端口  : 22 (容器内)"
echo "================================================"

# 阻塞以保持容器运行
exec tail -f /dev/null
