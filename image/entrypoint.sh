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

# 启动 sshd(前台守护 → 后台跑,容器用 tail 续命)
/usr/sbin/sshd

echo "================================================"
echo " SSH 虚拟机已就绪"
echo " 用户名: $USER"
echo " 端口  : 22 (容器内)"
echo "================================================"

# 阻塞以保持容器运行
exec tail -f /dev/null
