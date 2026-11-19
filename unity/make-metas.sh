#!/usr/bin/env bash
# Writes a Unity .meta file for every file and folder in the package and the demo's Assets that
# doesn't have one yet. Unity would make these itself on first import, but with random GUIDs on each
# machine, so they're made once here and committed. Existing .meta files are never touched.
#
# Run it after adding a file to unity/: bash unity/make-metas.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

guid() { cat /proc/sys/kernel/random/uuid 2>/dev/null | tr -d '-' || powershell.exe -NoProfile -Command "[guid]::NewGuid().ToString('N')" | tr -d '\r'; }

tail_lines() { printf '  userData: \n  assetBundleName: \n  assetBundleVariant: \n'; }

write_meta() {
  local target="$1" meta="$1.meta"
  [ -e "$meta" ] && return 0
  local id; id="$(guid)"
  {
    printf 'fileFormatVersion: 2\nguid: %s\n' "$id"
    if [ -d "$target" ]; then
      printf 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
    else
      case "$target" in
        *.cs)      printf 'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n' ;;
        *.asmdef)  printf 'AssemblyDefinitionImporter:\n  externalObjects: {}\n' ;;
        */package.json) printf 'PackageManifestImporter:\n  externalObjects: {}\n' ;;
        *.asset)   printf 'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n' ;;
        *.xml|*.txt|*.md|*.json) printf 'TextScriptImporter:\n  externalObjects: {}\n' ;;
        *.dll)
          # the server SDK only exists in the editor, a Dedicated Server build (UNITY_SERVER) or a build
          # with HAKUTAKU_SERVER defined, so a client build can never contain it
          printf 'PluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n'
          if [[ "$target" == *Server.dll ]]; then
            printf '  defineConstraints:\n  - UNITY_EDITOR || UNITY_SERVER || HAKUTAKU_SERVER\n'
          else
            printf '  defineConstraints: []\n'
          fi
          printf '  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n'
          printf '  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 1\n      settings: {}\n' ;;
        *)         printf 'DefaultImporter:\n  externalObjects: {}\n' ;;
      esac
    fi
    tail_lines
  } > "$meta"
  echo "made $meta"
}

for root in "$here/com.hakutaku.sdk" "$here/HakutakuDemo/Assets"; do
  [ -d "$root" ] || continue
  # the package root itself has no meta (it isn't an asset); everything inside it does. same for Assets
  find "$root" -mindepth 1 -not -name '*.meta' -print0 | while IFS= read -r -d '' path; do write_meta "$path"; done
done
