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
      "$adb_bin" connect "$address" || true
      if ! select_quest; then
        # ADB's background server can retain a broken route after Wi-Fi wakes.
        "$adb_bin" kill-server
        "$adb_bin" start-server
        "$adb_bin" connect "$address" || true
      fi
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

restore_project_config() {
  if [[ -n "${project_config_backup:-}" && -f "$project_config_backup" ]]; then
    cp "$project_config_backup" "$project_dir/project.godot"
    rm -f "$project_config_backup"
  fi
  if [[ -n "${manifest_backup:-}" && -f "$manifest_backup" ]]; then
    cp "$manifest_backup" "$main_manifest"
    rm -f "$manifest_backup"
  fi
  if [[ -n "${preset_backup:-}" && -f "$preset_backup" ]]; then
    cp "$preset_backup" "$project_dir/export_presets.cfg"
    rm -f "$preset_backup"
  fi
  if [[ "${staged_test_data:-false}" == true ]]; then
    rm -f "$project_dir/TestData/MW2.PRJ"
    rmdir "$project_dir/TestData"
    staged_test_data=false
  fi
}

install_game_data() {
  local staging_path=/sdcard/Download/MechRewired/MW2.PRJ local_hash remote_hash
  if [[ ! -f "$game_data_path" ]]; then
    printf 'No local MW2.PRJ copied. Existing private data is preserved; import your own file in the headset if needed.\n'
    return
  fi
  local_hash="$(shasum -a 256 "$game_data_path" | awk '{print $1}')"
  remote_hash="$("$adb_bin" -s "$quest_serial" shell sha256sum "$staging_path" 2>/dev/null | awk '{print $1}' || true)"
  if [[ "$local_hash" == "$remote_hash" ]]; then
    printf 'Downloads/MechRewired/MW2.PRJ already matches your local file.\n'
  else
    "$adb_bin" -s "$quest_serial" shell mkdir -p /sdcard/Download/MechRewired
    "$adb_bin" -s "$quest_serial" push "$game_data_path" "$staging_path"
    remote_hash="$("$adb_bin" -s "$quest_serial" shell sha256sum "$staging_path" | awk '{print $1}')"
  fi
  if [[ "$local_hash" != "$remote_hash" ]]; then
    printf 'Quest game data checksum does not match the local file.\n' >&2
    exit 1
  fi
  printf 'Verified your file in Downloads/MechRewired. In MechRewired select IMPORT MW2.PRJ to copy it into private app storage.\n'
}

fetch_benchmarks() {
  local destination="$repo_dir/local/quest-benchmarks/$(date +%Y%m%d-%H%M%S)"
  local app_destination="$destination/app-storage"
  local downloads_destination="$destination/downloads"
  local remote_path local_destination
  connect_quest
  mkdir -p "$app_destination" "$downloads_destination"
  # App-specific external storage remains readable over ADB even when Android
  # prevents a release app from writing into the public Downloads directory.
  for remote_path in /storage/emulated/0/Android/data/uk.co.deanthecoder.mechrewired/files/benchmarks \
    /sdcard/Download/MechRewired/benchmarks; do
    if [[ "$remote_path" == /storage/emulated/0/Android/data/* ]]; then
      local_destination="$app_destination"
    else
      local_destination="$downloads_destination"
    fi
    if "$adb_bin" -s "$quest_serial" shell test -d "$remote_path" >/dev/null 2>&1; then
      if ! "$adb_bin" -s "$quest_serial" pull "$remote_path/" "$local_destination/"; then
        printf 'Could not read Quest benchmark mirror: %s\n' "$remote_path" >&2
      fi
    fi
  done
  if find "$app_destination" -type f -name '*.log' -print -quit | rg -q .; then
    printf 'Fetched preferred app-storage benchmark logs to %s\n' "$app_destination"
    if find "$downloads_destination" -type f -name '*.log' -print -quit | rg -q .; then
      printf 'Fetched separate Downloads mirror to %s\n' "$downloads_destination"
    fi
    printf 'Quest benchmark files are under %s\n' "$destination"
  elif find "$downloads_destination" -type f -name '*.log' -print -quit | rg -q .; then
    printf 'App-storage mirror is unavailable; fetched Downloads fallback to %s\n' "$downloads_destination"
    printf 'Quest benchmark files are under %s\n' "$destination"
  else
    rmdir "$app_destination" "$downloads_destination" "$destination" 2>/dev/null || true
    printf 'No ADB-readable combat benchmark files were found. Run the benchmark once with this build, then retry. The app-private user:// copy is not readable from a Release APK over ADB.\n' >&2
    printf 'Expected mirrors: /storage/emulated/0/Android/data/uk.co.deanthecoder.mechrewired/files/benchmarks or /sdcard/Download/MechRewired/benchmarks\n' >&2
    exit 1
  fi
}

build_quest() {
  local output_path="$1" signing_dir signing_key signing_password export_status export_log quest_dotnet_dir
  # IDE shells can force MSBuild from a different SDK than the dotnet executable.
  # Let the selected dotnet SDK resolve its own tools for this build process.
  unset MSBUILD_EXE_PATH MSBuildSDKsPath MSBuildExtensionsPath
  unset DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER
  # Godot prefers this installation on macOS even when PATH selects a private SDK.
  # Align its in-process SDK discovery and child publish process with that choice.
  if [[ "$(uname -s)" == Darwin && -x /usr/local/share/dotnet/dotnet ]]; then
    quest_dotnet_dir=/usr/local/share/dotnet
    if [[ "$(uname -m)" == x86_64 && -x /usr/local/share/dotnet/x64/dotnet ]]; then
      quest_dotnet_dir=/usr/local/share/dotnet/x64
    fi
    export PATH="$quest_dotnet_dir:$PATH"
    export DOTNET_ROOT="$quest_dotnet_dir"
    export DOTNET_ROOT_ARM64="$quest_dotnet_dir"
    export DOTNET_ROOT_X64="$quest_dotnet_dir"
    export DOTNET_HOST_PATH="$quest_dotnet_dir/dotnet"
    printf 'Quest .NET SDK: %s (%s)\n' "$("$quest_dotnet_dir/dotnet" --version)" "$quest_dotnet_dir"
  fi
  signing_dir="${QUEST_SIGNING_DIR:-$HOME/Library/Application Support/MechRewired/signing}"
  signing_key="$signing_dir/mechrewired-release.keystore"
  signing_password="$signing_dir/keystore-password"
  if [[ ! -f "$signing_key" ]]; then
    if [[ -e "$signing_password" ]]; then
      printf 'Signing password exists without its key. Restore the original key before building.\n' >&2
      exit 1
    fi
    mkdir -p "$signing_dir"
    chmod 700 "$signing_dir"
    (umask 077; openssl rand -hex 32 > "$signing_password")
    "$JAVA_HOME/bin/keytool" -genkeypair -keystore "$signing_key" \
      -storetype JKS -alias mechrewired -keyalg RSA -keysize 2048 -validity 10000 \
      -dname 'CN=DeanTheCoder, O=MechRewired, C=GB' \
      -storepass:file "$signing_password" -keypass:file "$signing_password"
    chmod 600 "$signing_key"
  fi
  if [[ ! -f "$signing_password" ]]; then
    printf 'Signing password missing. Restore the original password file before building.\n' >&2
    exit 1
  fi
  export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$signing_key"
  export GODOT_ANDROID_KEYSTORE_RELEASE_USER=mechrewired
  export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$(cat "$signing_password")"
  if [[ ! -f "$project_dir/MechRewired.sln" ]]; then
    printf 'Missing project-local MechRewired.sln required for Godot Android .NET export.\n' >&2
    exit 1
  fi
  mkdir -p "$(dirname "$output_path")"
  project_config_backup="$(mktemp "${TMPDIR:-/tmp}/mechrewired-project.XXXXXX")"
  cp "$project_dir/project.godot" "$project_config_backup"
  trap restore_project_config EXIT
  if [[ "${QUEST_INCLUDE_TEST_DATA:-0}" == 1 ]]; then
    if [[ ! -f "$game_data_path" || -e "$project_dir/TestData" ]]; then
      printf 'Private test data requires a local MW2.PRJ and no existing project TestData directory.\n' >&2
      exit 1
    fi
    preset_backup="$(mktemp "${TMPDIR:-/tmp}/mechrewired-presets.XXXXXX")"
    cp "$project_dir/export_presets.cfg" "$preset_backup"
    mkdir "$project_dir/TestData"
    staged_test_data=true
    cp "$game_data_path" "$project_dir/TestData/MW2.PRJ"
    perl -0pi -e 's/include_filter=""/include_filter="TestData\/MW2.PRJ"/g; s/,\*\*\/MW2.PRJ,\*\*\/mw2.prj//g' "$project_dir/export_presets.cfg"
    printf 'Including your MW2.PRJ in this private test APK. Do not publish it to Production.\n'
  fi
  if [[ ! -f "$project_dir/android/.build_version" || ! -f "$project_dir/android/build/gradlew" ]]; then
    printf 'Installing the matching Android build template.\n'
    "$godot_bin" --headless --path "$project_dir" --xr-mode off \
      --install-android-build-template --export-release 'Quest Alpha' "$output_path"
  fi
  main_manifest="$project_dir/android/build/src/main/AndroidManifest.xml"
  manifest_backup="$(mktemp "${TMPDIR:-/tmp}/mechrewired-manifest.XXXXXX")"
  cp "$main_manifest" "$manifest_backup"
  # Meta requires head tracking; merge required=true into the generated manifest.
  if ! rg -q 'android.hardware.vr.headtracking' "$main_manifest"; then
    perl -0pi -e 's#(<supports-screens)#<uses-feature android:name="android.hardware.vr.headtracking" android:required="true" android:version="1" />\n\n    $1#' "$main_manifest"
  fi
  export_log="$output_path.export.log"
  export_status=0
  "$godot_bin" --headless --path "$project_dir" --xr-mode off \
    --export-release 'Quest Alpha' "$output_path" 2>&1 | tee "$export_log" || export_status=$?
  # Godot can return success after a failed managed publish and write an incomplete APK.
  if [[ "$export_status" != 0 ]] || rg -q 'Export \.NET Project:|Failed to build project\. Check MSBuild' "$export_log"; then
    printf 'Quest Release export failed. See %s and the Godot MSBuild panel for details.\n' "$export_log" >&2
    exit 1
  fi
  if [[ "${QUEST_INCLUDE_TEST_DATA:-0}" == 1 ]]; then
    python3 "$repo_dir/scripts/validate-quest-apk.py" "$output_path" --require-test-data
  else
    python3 "$repo_dir/scripts/validate-quest-apk.py" "$output_path"
  fi
  restore_project_config
  trap - EXIT
  printf 'Built release APK: %s\n' "$output_path"
}

case "${1:-install}" in
  connect)
    connect_quest enable_wifi
    printf 'Quest connected: %s\n' "$quest_serial"
    ;;
  share)
    build_quest "$project_dir/builds/MechRewired-Alpha.apk"
    printf 'Nothing was uploaded. Publish this APK to the private Meta Alpha channel separately to update testers.\n'
    printf 'Back up the signing directory securely: %s\n' "${QUEST_SIGNING_DIR:-$HOME/Library/Application Support/MechRewired/signing}"
    ;;
  build|install)
    if [[ "${1:-install}" == install ]]; then
      connect_quest
    fi
    build_quest "$apk_path"
    if [[ "${1:-install}" == install ]]; then
      if ! install_result="$("$adb_bin" -s "$quest_serial" install -r "$apk_path" 2>&1)"; then
        printf '%s\n' "$install_result" >&2
        if [[ "$install_result" == *INSTALL_FAILED_UPDATE_INCOMPATIBLE* ]]; then
          printf 'The installed copy uses a different signing key. Migration requires backing up its private MW2.PRJ and your approval before removing it. Nothing was uninstalled or cleared.\n' >&2
        fi
        exit 1
      fi
      printf '%s\n' "$install_result"
      install_game_data
      printf 'Installed release APK locally. No Meta channel or tester build was changed.\n'
    fi
    ;;
  data)
    connect_quest
    install_game_data
    ;;
  fetch-benchmarks)
    fetch_benchmarks
    ;;
  *)
    printf 'Usage: scripts/quest.sh [build|install|connect|data|share|fetch-benchmarks]\n' >&2
    exit 2
    ;;
esac
