# 🐳 Docker 虚拟机网站

一个一键给用户开 Docker SSH 虚拟机的网站。用户点一下按钮,后端立即创建一个 Docker 容器,弹窗显示 **IP / 端口 / 用户名 / 密码**,用户用 SSH 客户端即可连接。**不持久化,销毁即清理**。

- **后端**:C# (.NET 10) + EF Core + SQLite + [Docker.DotNet](https://github.com/dotnet/Docker.DotNet)
- **前端**:Vue 3 + Vite + Naive UI
- **部署**:docker-compose 一键起

---

## ✨ 功能

**用户系统**(开放注册):
- `/login` 页注册 / 登录,Session + Cookie 维持登录态(7 天)
- 密码 PBKDF2 哈希,HttpOnly + SameSite=Strict cookie
- 路由守卫:未登录自动跳登录页

**用户侧**(`/`,需登录):
- 一键开机器,弹窗显示连接信息(IP/端口/用户名/密码),每项可一键复制
- 容器**绑定到用户**,只能看到和操作自己的容器
- 查看我的容器列表、自助销毁

**管理后台**(`/admin`,需登录且 `IsAdmin=true`):
- **名额管理**:设置全局名额池总数 / 一键重置已用数 / 给指定用户加额外名额
- 查看所有容器(总数 / 运行中)
- 查看所有用户(用户名 / 角色 / 容器数 / 个人加量)
- 强制销毁任意容器
- 清理孤儿记录(数据库有记录但 Docker 已删除的)

**名额限量**:
- admin 设置全局名额池(如 5)。所有用户共享,开一台扣 1
- 名额用完,用户开机器会返回 409,前端显示"今日名额已用完"
- admin 可以给指定用户额外加名额(全局池空了该用户仍能开)
- 名额只在 admin 手动重置时归零,**销毁容器不退名额**(避免反复刷)

**容器资源限制**(每台):
- CPU 1 核(`VM_CPU_CORES`)
- 内存 1G(`VM_MEMORY_MB`)
- 进程数 200(`VM_PIDS_LIMIT`,防 fork 炸弹)
- 磁盘 5G(`VM_DISK_SIZE`,**依赖宿主配额,见下文**)

**容器内环境**(Alpine 基础镜像):
- OpenSSH / bash / curl / wget / git / vim / nano
- python3 + pip / nodejs + npm
- sudo(免密,用户可在容器内 `sudo apk add xxx` 装包)
- 时区已设置为 Asia/Shanghai

---

## 🚀 一键部署(服务器)

### 前置要求
- 已安装 Docker 和 Docker Compose
- (可选)宿主机已装 Nginx,用于反代域名
- 服务器内存 ≥ 1GB
- 防火墙 / 安全组放行端口:
  - 直接访问 IP 用法:`8686`(网页)+ `20000-30000`(SSH 容器)
  - 域名反代用法:`80`/`443`(网页,由宿主 nginx 提供)+ `20000-30000`(SSH 容器)

### 步骤

```bash
# 1. 拉代码
git clone <你的仓库地址> docker-vm
cd docker-vm

# 2. 复制配置文件,改一下初始管理员账号密码
cp .env.example .env
vi .env   # 至少把 INITIAL_ADMIN_PASSWORD 改成强密码

# 3. 一键启动
docker compose up -d --build
```

首次启动时后端会自动 build Alpine SSH 镜像(`docker-vm-alpine:latest`),需要 1-2 分钟。查看进度:

```bash
docker compose logs -f backend
```

看到 `SSH 镜像构建完成` 后,访问 `http://服务器IP:8686/` 即可使用。

### 更新

```bash
git pull
docker compose up -d --build
```

### 用域名 + 宿主机 Nginx 反代(推荐生产用法)

如果不想让用户记 IP:端口,可以让宿主机的 Nginx 反代到一个域名(如 `vm.byboy.cc`)。本仓库默认就把内部 8686 锁定到 `127.0.0.1`,对外只能通过 Nginx 进来。

**1. 域名解析**:把 `vm.byboy.cc` 的 A 记录指向服务器公网 IP。

**2. 装 Nginx 反代配置**(仓库里已带模板):

```bash
# 拷到 nginx 站点目录
sudo cp deploy/nginx-vm.byboy.cc.conf /etc/nginx/conf.d/

# 测试语法 + 重载
sudo nginx -t && sudo nginx -s reload
```

> Ubuntu/Debian 用户如果用 `sites-available` + `sites-enabled` 模式,改成:
> ```bash
> sudo cp deploy/nginx-vm.byboy.cc.conf /etc/nginx/sites-available/vm.byboy.cc
> sudo ln -s /etc/nginx/sites-available/vm.byboy.cc /etc/nginx/sites-enabled/
> sudo nginx -t && sudo nginx -s reload
> ```

**3. 配置要点**(模板里已写好,这里说明):
- `proxy_pass http://127.0.0.1:8686` —— 反代到本仓库 docker compose 暴露的端口
- 透传 `X-Forwarded-Proto` —— 后端据此决定 Cookie 是否打 `Secure` 标志
- 反代超时 180s —— 因为首次创建容器要 build 镜像,可能慢

**4. 后续上 HTTPS**(强烈推荐):

```bash
sudo certbot --nginx -d vm.byboy.cc
```

certbot 会自动改 nginx 配置加上 443 + 证书,跑完就能用 `https://vm.byboy.cc/` 访问。此时后端的 Cookie 会自动变成 `Secure`(因为 `X-Forwarded-Proto=https`),`SameSite=Strict` 也生效。

**5. 验证**:
```bash
curl -I http://vm.byboy.cc/api/health
# 期望返回 200 + {"ok":true}
```


---

## ⚙️ 配置项(.env)

| 变量 | 默认值 | 说明 |
|---|---|---|
| `HOST_IP` | (自动探测) | 返回给用户的 IP。云主机建议手动写公网 IP |
| `PORT_MIN` | `20000` | SSH 端口范围下限 |
| `PORT_MAX` | `30000` | SSH 端口范围上限 |
| `INITIAL_ADMIN_USERNAME` | `admin` | **⚠️ 必改密码**。首启创建的初始管理员用户名 |
| `INITIAL_ADMIN_PASSWORD` | `change-me-please` | **⚠️ 必改**。初始管理员密码 |
| `SSH_USER` | `user` | 容器内创建的 SSH 用户名 |
| `SSH_IMAGE_NAME` | `docker-vm-alpine:latest` | SSH 镜像名 |
| `SSH_IMAGE_CONTEXT_DIR` | `/app/image` | 镜像构建目录(容器内路径) |
| `CORS_ORIGINS` | `*` | 跨域来源,逗号分隔 |
| `DOCKER_HOST` | `unix:///var/run/docker.sock` | Docker daemon 地址 |
| `VM_CPU_CORES` | `1` | 每台容器 CPU 核数(可小数) |
| `VM_MEMORY_MB` | `1024` | 每台容器内存上限(MB) |
| `VM_PIDS_LIMIT` | `200` | 每台容器进程/线程数上限 |
| `VM_DISK_SIZE` | `5g` | 每台容器磁盘配额(依赖宿主,见下文) |
| `QUOTA_INITIAL_TOTAL` | `5` | 首启初始化的全局名额总数(之后由 admin 管理) |

> `ADMIN_TOKEN` 配置项已废弃(向后兼容保留),新版管理后台改用登录账号的 `IsAdmin` 判断。

---

## 📡 API 一览

### 认证(公开)
| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/auth/register` | 注册,body: `{username, password}`,成功自动登录 |
| `POST` | `/api/auth/login` | 登录 |
| `POST` | `/api/auth/logout` | 登出 |
| `GET` | `/api/auth/me` | 当前登录用户 |
| `GET` | `/api/health` | 健康检查 |

### 用户侧(需登录 cookie)
| 方法 | 路径 | 说明 |
|---|---|---|
| `POST` | `/api/vm` | 创建容器(绑定到当前用户) |
| `GET` | `/api/vm` | 列出我的容器 |
| `GET` | `/api/vm/{key}` | 查单个详情(校验归属) |
| `DELETE` | `/api/vm/{key}` | 自助销毁 |

### 管理后台(需登录 + IsAdmin)
| 方法 | 路径 | 说明 |
|---|---|---|
| `GET` | `/api/admin/containers` | 列出全部容器 |
| `DELETE` | `/api/admin/containers/{key}` | 强制销毁 |
| `POST` | `/api/admin/cleanup-orphans` | 清理孤儿记录 |
| `GET` | `/api/admin/users` | 列出所有用户 |

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

deploy/                        # 外部宿主机的部署配置(可选)
└── nginx-vm.byboy.cc.conf     # 宿主 nginx 反代到本机 8686 的站点模板
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
2. **初始管理员密码必须改**:`.env` 里的 `INITIAL_ADMIN_PASSWORD=change-me-please` 是默认值,务必改成强密码。该账号只在首次启动(数据库为空)时创建一次,改完密码后服务会自动用新值创建。
3. **开放注册风险**:`/login` 页允许任何人注册账号并开机器。如果只给自己/朋友用,建议:
   - 在 nginx 前加 IP 白名单
   - 或修改 `backend/Endpoints/AuthEndpoints.cs` 关掉注册接口(注释 `/register` 路由)
4. **端口范围暴露**:容器端口(`PORT_MIN`-`PORT_MAX`)需要对外开放,云服务器请在安全组精确放行,不要开 `0-65535`。
5. **SSH 密码明文存储**:为支持"再次查看连接信息",容器 SSH 密码在 SQLite 中**明文存储**。如果担心,可改 `backend/Endpoints/VmEndpoints.cs` 删除返回与存储。
6. **Cookie 安全**:session cookie 是 `HttpOnly + SameSite=Strict`,HTTPS 环境下自动加 `Secure`。生产请配 HTTPS(在 nginx 前加反代或证书)。

---

## 💿 磁盘配额说明(重要)

`VM_DISK_SIZE=5g` 通过 docker 的 `--storage-opt size=5g` 实现,但**它依赖宿主机文件系统支持**,不是开箱即用。

**生效条件**(满足其一):
- 宿主 `/var/lib/docker` 所在文件系统是 **XFS**,且挂载时带了 `pquota` 选项
- 或文件系统是 **ext4**,且启用了 quota
- 且 docker storage driver 是 `overlay2`

**怎么验证是否生效**:
```bash
docker run --rm --storage-opt size=5g alpine df -h /
# 看 / 这一行的 Size:如果是 5G → 生效;如果是宿主磁盘大小 → 没生效
```

**没生效怎么办**:
- 容器仍能正常开,只是磁盘没限制(用户能写满宿主磁盘)
- 想真正生效,需要在宿主上重新配置 docker:`/etc/docker/daemon.json` 加 `"storage-opts": ["overlay2.override_kernel_check=true"]`,并把 `/var/lib/docker` 重新挂为带 quota 的 XFS。这是个有风险的运维操作,建议在装系统时就规划好。
- 折中方案:CPU/内存/PID 限制是肯定生效的,磁盘只靠"用户自觉 + 销毁回收"间接管控。

**怎么修改资源限制**:
改 `.env` 里的 `VM_CPU_CORES` / `VM_MEMORY_MB` / `VM_PIDS_LIMIT` / `VM_DISK_SIZE`,然后:
```bash
docker compose up -d      # 不用 --build,只重启
```
> ⚠️ 已开出来的容器**不会**应用新配置,只对之后新开的容器生效。

---

## 🎫 名额管理流程

1. **首次启动**:数据库初始化全局名额池为 `QUOTA_INITIAL_TOTAL`(默认 5)
2. **admin 登录后台** → "名额管理" Tab:
   - 看到 `总额度 / 已消耗 / 剩余`
   - 点"修改总额度"改 `Total`(已用不变,剩余 = 新 Total - 已用)
   - 点"一键重置"把已用归零(可选同时改 Total)
3. **给某用户加量**:"用户" Tab → 对应用户行"个人加量"列 → 点击设置数量和备注
4. **用户开机器**:优先消耗全局池,全局空了才消耗个人加量
5. **销毁容器不退名额**(防刷)—— 如需改成"销毁退还",改 `backend/Services/QuotaService.cs` 注释里那一行

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

**Q: 从旧版(< 2.0,无登录系统)升级?**
A: 数据库结构改了(新增 users / sessions 表,containers 加了 UserId 列),需要**清空数据库**重新初始化:
```bash
docker compose down
rm data/docker-vm.db    # 容器是非持久化的,删了无所谓
docker compose up -d --build
```
然后记得改 `.env` 里的 `INITIAL_ADMIN_PASSWORD`,首启会自动创建管理员账号。

---

## 📜 License

MIT
