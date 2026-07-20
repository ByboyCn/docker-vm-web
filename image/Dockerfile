# 用户虚拟机的基础镜像:Alpine + OpenSSH + 常用工具
FROM alpine:3.20

RUN apk add --no-cache \
        openssh \
        openrc \
        bash \
        curl \
        wget \
        git \
        vim \
        nano \
        tar \
        gzip \
        unzip \
        ca-certificates \
        python3 \
        py3-pip \
        nodejs \
        npm \
        sudo \
        tzdata \
    && cp /usr/share/zoneinfo/Asia/Shanghai /etc/localtime \
    && echo "Asia/Shanghai" > /etc/timezone \
    && ssh-keygen -A \
    && sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin yes/' /etc/ssh/sshd_config \
    && sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication yes/' /etc/ssh/sshd_config

# 入口脚本:从环境变量读取用户名/密码,创建用户并启动 sshd
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 22

CMD ["/entrypoint.sh"]
