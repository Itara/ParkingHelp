# ------------------------
# Build stage
# ------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

# ------------------------
# Runtime stage
# ------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# 필수 도구 설치 (Playwright와 브라우저 설치를 위해)
RUN apt-get update && apt-get install -y wget curl gnupg unzip fonts-liberation libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libxcomposite1 libxrandr2 libxdamage1 libxfixes3 libxkbcommon0 libxshmfence1 libgbm1 libasound2 libpangocairo-1.0-0 libx11-xcb1 libxss1 libgtk-3-0

# Playwright CLI 설치 (글로벌 dotnet 툴 경로 설정)
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global Microsoft.Playwright.CLI

# Chromium 브라우저 설치
RUN playwright install chromium

# 앱 복사 및 실행
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ParkingHelp.dll"]