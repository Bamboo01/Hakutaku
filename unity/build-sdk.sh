#!/usr/bin/env bash
# Builds the client and server SDKs (sdk/, sdk-server/) and copies the two DLLs into the Unity
# package (unity/com.hakutaku.sdk/Plugins/). Run it after any change to sdk/ or sdk-server/, then
# commit the DLLs with that change: Unity uses the copies in the package, not the source.
#
# Run from anywhere, in WSL or Git Bash: bash unity/build-sdk.sh
# Both SDKs target netstandard2.1, so any .NET SDK from 6 on can build them.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
plugins="$repo/unity/com.hakutaku.sdk/Plugins"

dotnet build "$repo/sdk/Hakutaku.Sdk.csproj" -c Release --nologo -v quiet
dotnet build "$repo/sdk-server/Hakutaku.Sdk.Server.csproj" -c Release --nologo -v quiet

mkdir -p "$plugins"
cp "$repo/sdk/bin/Release/netstandard2.1/Hakutaku.Sdk.dll" "$plugins/"
cp "$repo/sdk-server/bin/Release/netstandard2.1/Hakutaku.Sdk.Server.dll" "$plugins/"
echo "Copied Hakutaku.Sdk.dll and Hakutaku.Sdk.Server.dll into $plugins"
