#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot.app/Contents/MacOS/Godot}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk@17}"
mqdh_adb="/Applications/Meta Quest Developer Hub.app/Contents/Resources/bin/adb"
if [[ -x "$mqdh_adb" ]]; then
  adb_bin="${ADB_BIN:-$mqdh_adb}"
else
  adb_bin="${ADB_BIN:-$ANDROID_HOME/platform-tools/adb}"
fi
quest_address_file="${QUEST_ADDRESS_FILE:-$HOME/Library/Caches/MechRewired/quest-address}"
quest_serial="${ANDROID_SERIAL:-}"

remember_address() {
  mkdir -p "$(dirname "$quest_address_file")"
  printf '%s\n' "$1" > "$quest_address_file"
}

select_quest() {
  local devices candidates count
  devices="$("$adb_bin" devices -l)"
  if [[ -n "$quest_serial" ]]; then
    "$adb_bin" -s "$quest_serial" get-state >/dev/null 2>&1
    return
  fi
  # Prefer Wi-Fi when USB and Wi-Fi both expose the same headset.
  candidates="$(printf '%s\n' "$devices" | awk '$2 == "device" && /model:Quest/ && $1 ~ /:/ {print $1}')"
  if [[ -z "$candidates" ]]; then
    candidates="$(printf '%s\n' "$devices" | awk '$2 == "device" && /model:Quest/ {print $1}')"
  fi
  count="$(printf '%s\n' "$candidates" | awk 'NF {n++} END {print n+0}')"
  if [[ "$count" -gt 1 ]]; then
    printf 'Multiple Quests detected. Set ANDROID_SERIAL to the desired device.\n' >&2
    exit 1
  fi
  [[ "$count" == 1 ]] || return 1
  quest_serial="$candidates"
}

connect_quest() {
  local address="${QUEST_HOST:-}" ip
  if [[ -z "$address" && -f "$quest_address_file" ]]; then
    address="$(cat "$quest_address_file")"
  fi
  if ! select_quest; then
    if [[ -n "$address" ]]; then
      [[ "$address" == *:* ]] || address="$address:5555"
      "$adb_bin" connect "$address"
    fi
    if ! select_quest; then
      printf 'No Quest connected. Connect USB and accept debugging, then run scripts/quest.sh connect to enable Wi-Fi. For a changed IP, use QUEST_HOST=<headset-ip> scripts/quest.sh install.\n' >&2
      exit 1
    fi
  fi
  if [[ "$quest_serial" == *:* ]]; then
    remember_address "$quest_serial"
  else
    ip="$("$adb_bin" -s "$quest_serial" shell ip -4 addr show wlan0 | awk '/inet / {split($2,a,"/"); print a[1]; exit}')"
    if [[ -n "$ip" ]]; then
      remember_address "$ip:5555"
      if [[ "${1:-}" == enable_wifi ]]; then
        "$adb_bin" -s "$quest_serial" tcpip 5555
        # adbd needs a moment to restart before accepting a TCP connection.
        sleep 2
        "$adb_bin" connect "$ip:5555"
        quest_serial="$ip:5555"
        "$adb_bin" -s "$quest_serial" get-state >/dev/null
      fi
    elif [[ "${1:-}" == enable_wifi ]]; then
      printf 'Quest has no Wi-Fi address. Connect it to Wi-Fi first.\n' >&2
      exit 1
    fi
  fi
}
project_dir="$repo_dir/MechRewired"
apk_path="$project_dir/builds/MechRewired-Quest3.apk"
game_data_path="${MW2_PRJ:-$repo_dir/local/game-data/MW2.PRJ}"
package_id="uk.co.deanthecoder.mechrewired"

install_game_data() {
  local staging_path=/data/local/tmp/MechRewired-MW2.PRJ local_hash remote_hash
  if [[ ! -f "$game_data_path" ]]; then
    if "$adb_bin" -s "$quest_serial" shell run-as "$package_id" test -s files/game-data/MW2.PRJ; then
      printf 'Keeping existing Quest game data (no local MW2.PRJ found).\n'
      return
    fi
    printf 'Game data missing. Set MW2_PRJ to your compatible MW2.PRJ file and run scripts/quest.sh data.\n' >&2
    exit 1
  fi
  local_hash="$(shasum -a 256 "$game_data_path" | awk '{print $1}')"
  remote_hash="$("$adb_bin" -s "$quest_serial" shell run-as "$package_id" sha256sum files/game-data/MW2.PRJ 2>/dev/null | awk '{print $1}' || true)"
  if [[ "$local_hash" == "$remote_hash" ]]; then
    printf 'Quest game data already matches the local MW2.PRJ.\n'
    return
  fi
  "$adb_bin" -s "$quest_serial" push "$game_data_path" "$staging_path"
  "$adb_bin" -s "$quest_serial" shell run-as "$package_id" mkdir -p files/game-data
  "$adb_bin" -s "$quest_serial" shell run-as "$package_id" cp "$staging_path" files/game-data/MW2.PRJ
  "$adb_bin" -s "$quest_serial" shell rm "$staging_path"
  remote_hash="$("$adb_bin" -s "$quest_serial" shell run-as "$package_id" sha256sum files/game-data/MW2.PRJ | awk '{print $1}')"
  if [[ "$local_hash" != "$remote_hash" ]]; then
    printf 'Quest game data checksum does not match the local file.\n' >&2
    exit 1
  fi
  printf 'Installed and verified Quest game data.\n'
}

case "${1:-install}" in
  connect)
    connect_quest enable_wifi
    printf 'Quest connected: %s\n' "$quest_serial"
    ;;
  build|install)
    if [[ "${1:-install}" == install ]]; then
      connect_quest
    fi
    if [[ ! -f "$project_dir/MechRewired.sln" ]]; then
      printf 'Missing MechRewired/MechRewired.sln; Godot Android .NET export requires the project-local solution.\n' >&2
      exit 1
    fi
    mkdir -p "$(dirname "$apk_path")"
    "$godot_bin" --headless --path "$project_dir" --xr-mode off \
      --export-debug 'Quest 3 (setup required)' "$apk_path"
    printf 'Built debug APK: %s\n' "$apk_path"
    if [[ "${1:-install}" == install ]]; then
      "$adb_bin" -s "$quest_serial" install -r "$apk_path"
      install_game_data
      printf 'Installed debug APK and game data on Quest.\n'
    fi
    ;;
  data)
    connect_quest
    install_game_data
    ;;
  *)
    printf 'Usage: scripts/quest.sh [build|install|connect|data]\n' >&2
    exit 2
    ;;
esac
