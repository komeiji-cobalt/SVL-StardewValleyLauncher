#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[ERROR] dotnet 未安装。请先安装 .NET SDK 后重试。"
  echo "        brew install --cask dotnet-sdk"
  exit 1
fi

cd "$ROOT_DIR"

echo "[1/4] restore SVL.Core.Platform"
dotnet restore SVL.Core.Platform/SVL.Core.Platform.csproj

echo "[2/4] restore SVL.Avalonia"
dotnet restore SVL.Avalonia/SVL.Avalonia.csproj

echo "[3/4] build SVL.Avalonia"
dotnet build SVL.Avalonia/SVL.Avalonia.csproj -c Debug

echo "[4/4] run migration tests"
dotnet test SVL.Migration.Tests/SVL.Migration.Tests.csproj -c Debug

echo "[DONE] Avalonia migration baseline validated on macOS."
