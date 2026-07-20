# 🐳 Docker 虚拟机网站

一个一键给用户开 Docker SSH 虚拟机的网站。用户点一下按钮,后端立即创建一个 Docker 容器,弹窗显示 **IP / 端口 / 用户名 / 密码**,用户用 SSH 客户端即可连接。**不持久化,销毁即清理**。

- **后端**:C# (.NET 10) + EF Core + SQLite + [Docker.DotNet](https://github.com/dotnet/Docker.DotNet)
- **前端**:Vue 3 + Vite + Naive UI
- **部署**:docker-compose 一键起

---

## ✨ 功能

**用户侧**(无需登录):
- 一键开机器,弹窗显示连接信息(IP/端口/用户名/密码),每项可一键复制
- 浏览器本地记住自己的 key,刷新页面自动恢复
- 查看我的容器列表、自助销毁

**管理后台**(`/admin`,需 token):
- 查看所有容器(总数 / 运行中)
- 强制销毁任意容器
- 清理孤儿记录(数据库有记录但 Docker 已删除的)

**容器内环境**(Alpine 基础镜像):
- OpenSSH / bash / curl / wget / git / vim / nano
- python3 + pip / nodejs + npm
- sudo(免密,用户可在容器内 `sudo apk add xxx` 装包)
- 时区已设置为 Asia/Shanghai

---

## 🚀 一键部署(服务器)

### 前置要求
- 已安装 Docker 和 Docker Compose
- 服务器内存 ≥ 1GB
- 防火墙 / 安全组放行端口:`80`(网页)+ `20000-30000`(SSH 容器)

### 步骤

```bash
# 1. 拉代码
git clone <你的仓库地址> docker-vm
cd docker-vm

# 2. 复制配置文件,改一下 ADMIN_TOKEN
cp .env.example .env
vi .env   # 至少把 ADMIN_TOKEN 改成随机串

# 3. 一键启动
docker compose up -d --build
```

首次启动时后端会自动 build Alpine SSH 镜像(`docker-vm-alpine:latest`),需要 1-2 分钟。查看进度:

```bash
docker compose logs -f backend
```

看到 `SSH 镜像构建完成` 后,访问 `http://服务器IP/` 即可使用。

### 更新

```bash
git pull
docker compose up -d --build
```

---

## ⚙️ 配置项(.env)

| 变量 | 默认值 | 说明 |
|---|---|---|
| `HOST_IP` | (自动探测) | 返回给用户的 IP。云主机建议手动写公网 IP |
| `PORT_MIN` | `20000` | SSH 端口范围下限 |
| `PORT_MAX` | `30000` | SSH 端口范围上限 |
| `ADMIN_TOKEN` | `change-me-...` | **⚠️ 必改**。管理后台访问凭证 |
| `SSH_USER` | `user` | 容器内创建的 SSH 用户名 |
| `SSH_IMAGE_NAME` | `docker-vm-alpine:latest` | SSH 镜像名 |
| `SSH_IMAGE_CONTEXT_DIR` | `/app/image` | 镜像构建目录(容器内路径) |
| `CORS_ORIGINS` | `*` | 跨域来源,逗号分隔 |
| `DOCKER_HOST` | `unix:///var/run/docker.sock` | Docker daemon 地址 |

生成强 token:
```bash
openssl rand -hex 32
```

---

## 📡 API 一览

### 用户侧(无需认证)
| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/vm` | 创建容器 |
| `GET` | `/api/vm` | 列出我的容器(header:`X-VM-Key: k1,k2`) |
| `GET` | `/api/vm/{key}` | 查单个详情 |
| `DELETE` | `/api/vm/{key}` | 自助销毁 |
| `GET` | `/api/health` | 健康检查 |

### 管理后台(header:`Authorization: Bearer <ADMIN_TOKEN>`)
| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/admin/containers` | 列出全部容器 |
| `DELETE` | `/api/admin/containers/{key}` | 强制销毁 |
| `POST` | `/api/admin/cleanup-orphans` | 清理孤儿记录 |

---

## 🗂 项目结构

```
docker-vm/
├── docker-compose.yml         # 一键部署
├── .env.example               # 配置模板
├── backend/                   # .NET 10 后端
│   ├── Dockerfile
│   ├── DockerVm.Api.csproj
│   ├── Program.cs             # 入口 + 服务注册 + 自动建镜像
│   ├── Models/VmContainer.cs  # EF Core 实体
│   ├── Data/AppDbContext.cs   # SQLite 上下文
│   ├── Dtos/VmDto.cs
│   ├── Options/AppOptions.cs  # 配置类
│   ├── Services/              # DockerService / PortAllocator / HostIpDetector / SshImageBuilder / TarHelper
│   └── Endpoints/             # VmEndpoints(用户)/ AdminEndpoints(管理)
├── frontend/                  # Vue 3 + Naive UI
│   ├── Dockerfile             # 多阶段:node 构建 → nginx 托管
│   ├── nginx.conf             # 含 /api 反代
│   └── src/
│       ├── api/index.ts       # axios 封装 + key/token 管理
│       ├── components/ConnectionDialog.vue
│       └── pages/{Home,Admin}.vue
└── image/                     # Alpine SSH 镜像(后端启动时自动 build)
    ├── Dockerfile
    └── entrypoint.sh
```

---

## 🛠 本地开发

### 后端
```bash
cd backend
dotnet restore
dotnet run     # 监听 http://localhost:5000
```
> 本地开发时后端会尝试连 `unix:///var/run/docker.sock`,Linux/Mac 直接可用,Windows 需改 `DOCKER_HOST` 指向 Docker Desktop 的命名管道或 TCP 端口。

### 前端
```bash
cd frontend
npm install
npm run dev    # 监听 http://localhost:5173,/api 自动代理到 5000
```

---

## 🔒 安全须知

1. **docker.sock 挂载 = 宿主机 root 权限**:本服务必须部署在**你完全可信**的服务器上,不要在多人共享的机器上开放公网访问。
2. **ADMIN_TOKEN 必须改**:`change-me-to-random-string` 是默认值,务必替换为随机长字符串。
3. **端口范围暴露**:容器端口(`PORT_MIN`-`PORT_MAX`)需要对外开放,云服务器请在安全组精确放行,不要开 `0-65535`。
4. **密码明文存储**:为支持"凭 key 再次查看",SSH 密码在 SQLite 中**明文存储**。如果担心,可改 `Endpoints/VmEndpoints.cs` 删除返回与存储。
5. **无用户认证**:用户侧任何访客都能开机器,建议在内网部署,或在 nginx 前加 IP 白名单 / Basic Auth。

---

## ❓ 常见问题

**Q: 创建容器失败,日志显示 docker socket 连不上?**
A: 检查 `docker compose` 是否成功挂载 `/var/run/docker.sock`,以及宿主机 dockerd 是否在运行。

**Q: 创建后 SSH 连不上?**
A: 三步排查:
1. `docker ps` 看容器是否 `running`
2. `docker logs <容器名>` 看 entrypoint 是否报错
3. 检查防火墙/安全组是否放行对应的宿主端口

**Q: 返回的 IP 是内网地址,我想让外网用户连?**
A: 在 `.env` 设置 `HOST_IP=你的公网IP`。

**Q: 怎么修改容器里预装的工具?**
A: 改 `image/Dockerfile`,然后:
```bash
docker rmi docker-vm-alpine:latest
docker compose restart backend   # 后端会自动重新 build
```

---

## 📜 License

MIT
