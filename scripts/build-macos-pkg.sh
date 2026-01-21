#!/bin/bash

# macOS (.pkg) インストーラー作成スクリプト

APP_NAME="Sidecar.Client"
PROJECT_PATH="src/Sidecar.Client/Sidecar.Client.csproj"
OUTPUT_DIR="publish/macos"
PKG_NAME="Sidecar.Client-Installer.pkg"

echo "🚀 Building ${APP_NAME} for Mac Catalyst..."

# 1. パブリッシュ (Mac Catalyst版)
dotnet publish "${PROJECT_PATH}" \
    -f net10.0-maccatalyst \
    -c Release \
    -p:CreatePackage=false \
    -o "${OUTPUT_DIR}"

if [ $? -ne 0 ]; then
    echo "❌ Build failed."
    exit 1
fi

APP_PATH="${OUTPUT_DIR}/${APP_NAME}.app"

# 2. .pkg インストーラーの作成
echo "📦 Creating installer package..."
productbuild --component "${APP_PATH}" /Applications "${PKG_NAME}"

if [ $? -eq 0 ]; then
    echo "✅ Success! Installer created: ${PKG_NAME}"
else
    echo "❌ Failed to create installer."
    exit 1
fi
