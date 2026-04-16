FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "src/KernelPrint.Server/KernelPrint.Server.csproj"
RUN dotnet publish "src/KernelPrint.Server/KernelPrint.Server.csproj" -c Release -o /app/publish

# Install Linux deps Chromium needs for Playwright.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libnss3 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2 \
    libpango-1.0-0 \
    libcairo2 \
    libatspi2.0-0 \
    libx11-xcb1 \
    libxext6 \
    libx11-6 \
    ca-certificates \
    fonts-liberation \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Download Playwright Chromium into default cache path.
RUN chmod +x /app/publish/playwright.sh && /app/publish/playwright.sh install chromium

WORKDIR /app/publish
EXPOSE 5294

ENV ASPNETCORE_URLS=http://+:5294

ENTRYPOINT ["dotnet", "KernelPrint.Server.dll"]
