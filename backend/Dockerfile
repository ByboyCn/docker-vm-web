# ---------- 阶段 1:构建 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先拷 csproj 利用缓存
COPY DockerVm.Api.csproj ./
RUN dotnet restore

# 拷源码并发布
COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# ---------- 阶段 2:运行 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# 装 e2fsprogs(提供 mkfs.ext4,后端创建 loop 卷时用)
# truncate 由 coreutils 自带
RUN apt-get update && apt-get install -y --no-install-recommends \
        e2fsprogs \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# 环境默认值(可在 docker-compose / .env 覆盖)
ENV ASPNETCORE_URLS=http://+:5000 \
    DOTNET_PRINT_ERRORS=true \
    DB_PATH=/data/docker-vm.db

EXPOSE 5000

ENTRYPOINT ["dotnet", "DockerVm.Api.dll"]
