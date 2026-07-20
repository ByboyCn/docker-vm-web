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

# 装 docker CLI 不必要,我们用 Docker.DotNet 通过 sock 直接交互
# 但为了让后端能 build alpine 镜像(image 目录会被挂进来),要确保 sock 可访问即可

COPY --from=build /app/publish ./

# 环境默认值(可在 docker-compose / .env 覆盖)
ENV ASPNETCORE_URLS=http://+:5000 \
    DOTNET_PRINT_ERRORS=true \
    DB_PATH=/data/docker-vm.db

EXPOSE 5000

ENTRYPOINT ["dotnet", "DockerVm.Api.dll"]
