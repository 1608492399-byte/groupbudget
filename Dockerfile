# ASP.NET Core 8 容器：Vercel 以 Serverless Function 方式运行
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先还原依赖，代码变更时可复用这一层缓存
COPY GroupBudget.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# 双保险：真实数据库绝不打进镜像（.dockerignore 已排除，这里再兜一次）
RUN rm -f /app/publish/app.db /app/publish/app.db-shm /app/publish/app.db-wal

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
# Vercel 默认把流量路由到 80；容器内也允许通过 PORT 覆盖
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80

ENTRYPOINT ["dotnet", "GroupBudget.dll"]
