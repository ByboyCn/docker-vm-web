# 镜像变更后重新 build

本目录(`image/`)是用户虚拟机的 Alpine SSH 镜像源。后端启动时**只在镜像不存在时**才 build。

所以改了 `Dockerfile` 或 `entrypoint.sh` 后,需要**手动删除旧镜像**让后端重新 build:

```bash
# 1. 删除旧镜像
docker rmi docker-vm-alpine:latest

# 2. 重启 backend,它会自动 rebuild
docker compose restart backend

# 3. 看构建日志
docker compose logs -f backend | grep -A2 "build"
```

build 完成后,用户新开的机器会用新镜像。已经开出来的旧机器不受影响(它们用的是旧镜像层)。

## 已开出来的旧机器怎么办?

如果想让旧机器也用新镜像,只能销毁重开(数据会丢,但本来就是临时容器):
- 在管理后台 → "所有容器" → 强制销毁
- 或让用户在首页 → "我的容器" → 销毁
