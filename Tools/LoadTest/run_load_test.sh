#!/bin/bash
# Digital twin load test: one command from tests to a results table.
#
#   Tools/LoadTest/run_load_test.sh [options]
#
#   --skip-tests     do not run the existing editor and play-mode tests
#   --skip-build     reuse Builds/LoadTest instead of rebuilding it
#   --quick          standard scenarios only (about 12 minutes)
#   --only NAME      run one scenario (see SCENARIOS below)
#
# Needs: Unity (version from ProjectSettings/ProjectVersion.txt, installed
# through Unity Hub), Google Chrome, python3. The project must not be open in
# the Unity editor. Results go to Tools/LoadTest/results/<timestamp>/, with
# summary.md as the readable report. See USER_GUIDE.md (section 11) for the options.
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
KIT="$ROOT/Tools/LoadTest"
VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
PORT=8771
OUT="$KIT/results/$(date +%Y-%m-%d_%H%M)"
SERVE="$(mktemp -d)/serve"
RUN_TESTS=1; RUN_BUILD=1; QUICK=0; ONLY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --skip-tests) RUN_TESTS=0 ;;
    --skip-build) RUN_BUILD=0 ;;
    --quick) QUICK=1 ;;
    --only) ONLY="$2"; shift ;;
    *) echo "Unknown option $1"; exit 2 ;;
  esac
  shift
done
mkdir -p "$OUT"
SUMMARY="$OUT/summary.md"
[ -x "$UNITY" ] || { echo "Unity $VERSION not found at $UNITY"; exit 1; }
[ -x "$CHROME" ] || { echo "Google Chrome not found"; exit 1; }

ANDROID_UA="Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Mobile Safari/537.36"

# name | query string (or timing page) | window width | height | phone UA? | timeout s | quick set?
SCENARIOS=(
  "startup|timing:runs=4|1440|900|no|180|yes"
  "steady_wide|loadtest=30,120,500,1000,2000,3000&hz=2&alarms=0|1440|900|no|500|yes"
  "storm_wide|loadtest=30,120,500,1000,2000,3000,5000&hz=2&alarms=0.05|1440|900|no|600|yes"
  "storm_phone|loadtest=30,120,500,1000,2000&hz=2&alarms=0.05|500|1000|yes|500|yes"
  "dropout_wide|loadtest=1000,3000&hz=2&alarms=0&dropout=7|1440|900|no|300|no"
  "hd_storm_wide|loadtest=30,1000,3000&hz=2&alarms=0.05&hd=1|1440|900|no|300|no"
  "soak_1000|loadtest=1000&hz=2&alarms=0.05&repeat=30&measure=20|1440|900|no|1500|no"
)

log() { echo "[$(date +%H:%M:%S)] $*"; }

run_unity() { # name method needsQuit timeout
  local args=(-batchmode -projectPath "$ROOT" -executeMethod "$2" -logFile "$OUT/unity_$1.log")
  [ "$3" = "1" ] && args+=(-quit)
  local start=$(date +%s)
  "$UNITY" "${args[@]}" >/dev/null 2>&1 &
  local pid=$!
  ( sleep "$4"; kill -9 $pid 2>/dev/null ) >/dev/null 2>&1 & local watchdog=$!
  wait $pid; local code=$?
  kill $watchdog 2>/dev/null; wait $watchdog 2>/dev/null
  echo "$code $(( $(date +%s) - start ))"
}

if [ $RUN_TESTS = 1 ]; then
  log "Running existing project tests"
  echo "## Existing tests" >> "$SUMMARY"; echo >> "$SUMMARY"
  echo "| Test | Exit | Seconds | Result line |" >> "$SUMMARY"; echo "|---|---:|---:|---|" >> "$SUMMARY"
  # Edit-mode unit tests (NUnit, Assets/Editor/Tests), through Unity's own runner.
  start=$(date +%s)
  "$UNITY" -batchmode -projectPath "$ROOT" -runTests -testPlatform EditMode \
    -testResults "$OUT/editmode.xml" -logFile "$OUT/unity_EditMode.log" >/dev/null 2>&1
  code=$?
  line="$(head -3 "$OUT/editmode.xml" 2>/dev/null | grep -o -m1 'result="[A-Za-z]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"')"
  log "  EditModeTests exit=$code ($(( $(date +%s) - start )) s) $line"
  echo "| EditModeTests | $code | $(( $(date +%s) - start )) | ${line:-none} |" >> "$SUMMARY"
  # name | method | needs -quit | timeout. Add new tests here.
  for t in \
    "ProjectValidator|DigitalTwinProjectValidator.Validate|1|900" \
    "LayoutStressValidator|TelemetryLayoutStressValidator.Validate|1|600" \
    "FeedbackStressTest|FeedbackStressTest.Run|0|900" \
    "MachineViewModeLayout|MachineViewModeLayoutTest.Run|0|600" \
    "SelectionOutlineRender|SelectionOutlineRenderTest.Run|0|600" \
    "ConveyorFlowPlay|ConveyorFlowPlayTest.Run|0|600" \
    "FeedbackPlay|FeedbackPlayTest.Run|0|600" \
    "TelemetryDetailPanelPlay|TelemetryDetailPanelPlayTest.Run|0|600" \
    "ServiceModePlay|ServiceModePlayTest.Run|0|600" \
    "PortraitServiceModePlay|PortraitServiceModePlayTest.Run|0|600" \
    "LeaderLineStability|LeaderLineStabilityPlayTest.Run|0|600" \
    "LeaderLineOpacity|LeaderLineOpacityPlayTest.Run|0|600"
  do
    IFS='|' read -r name method quit limit <<< "$t"
    read -r code secs <<< "$(run_unity "$name" "$method" "$quit" "$limit")"
    line="$(grep -h -m1 -E '_RESULT|LAYOUT-TEST: all|OUTLINE-TEST: done|PLAY-TEST t=30' "$OUT/unity_$name.log" | cut -c1-110)"
    log "  $name exit=$code ($secs s) $line"
    echo "| $name | $code | $secs | ${line:-none} |" >> "$SUMMARY"
  done
  echo >> "$SUMMARY"
fi

if [ $RUN_BUILD = 1 ]; then
  log "Building Builds/LoadTest"
  read -r code secs <<< "$(run_unity build SensorLoadTestBuild.BuildWebGL 1 1800)"
  log "  build exit=$code ($secs s) $(grep -h LOADTEST_BUILD "$OUT/unity_build.log")"
  # The release build settings are reapplied on every build; keep the
  # build profile asset unchanged in git.
  git -C "$ROOT" checkout -- "Assets/Settings/Build Profiles/Web.asset" 2>/dev/null
  [ "$code" = "0" ] || { echo "Build failed; see $OUT/unity_build.log"; exit 1; }
fi

[ -f "$ROOT/Builds/LoadTest/index.html" ] || { echo "No Builds/LoadTest; run without --skip-build"; exit 1; }
mkdir -p "$SERVE"
cp -R "$ROOT/Builds/LoadTest" "$SERVE/LoadTest"
cp "$KIT/timing.html" "$SERVE/"
( cd /tmp && exec python3 -m http.server $PORT --directory "$SERVE" ) >/dev/null 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null; rm -rf "$(dirname "$SERVE")"' EXIT
sleep 1

echo "## Load scenarios" >> "$SUMMARY"; echo >> "$SUMMARY"
echo "Machine: $(sysctl -n machdep.cpu.brand_string), $(( $(sysctl -n hw.memsize) / 1073741824 )) GB, $("$CHROME" --version)" >> "$SUMMARY"
echo "Served from localhost (download time excluded). Commit $(git -C "$ROOT" rev-parse --short HEAD)." >> "$SUMMARY"; echo >> "$SUMMARY"
# On battery, Chrome and macOS cap pages at 30 fps (15 with the phone cap),
# which hides the twin's real frame rate. Intake and frame spikes stay valid.
if pmset -g batt 2>/dev/null | grep -q "Battery Power"; then
  warning="WARNING: running on battery. Frame rates are capped at 30 fps by the browser; plug in for valid fps numbers."
  log "$warning"
  echo "**$warning**" >> "$SUMMARY"; echo >> "$SUMMARY"
fi

for s in "${SCENARIOS[@]}"; do
  IFS='|' read -r name query width height phone limit quick <<< "$s"
  [ -n "$ONLY" ] && [ "$ONLY" != "$name" ] && continue
  [ -z "$ONLY" ] && [ $QUICK = 1 ] && [ "$quick" != "yes" ] && continue
  if [[ "$query" == timing:* ]]; then
    url="http://localhost:$PORT/timing.html?build=LoadTest&${query#timing:}"
  else
    url="http://localhost:$PORT/LoadTest/index.html?$query"
  fi
  extra=(); [ "$phone" = "yes" ] && extra=(--user-agent="$ANDROID_UA" --touch-events=enabled)
  profile="$(mktemp -d)"
  chromelog="$OUT/$name.chrome.log"
  log "Scenario $name"
  # A throwaway profile, with throttling off so frames keep rendering when
  # the window is behind others.
  "$CHROME" --user-data-dir="$profile" --no-first-run --no-default-browser-check \
    --disable-backgrounding-occluded-windows --disable-renderer-backgrounding \
    --disable-background-timer-throttling --disable-features=CalculateNativeWinOcclusion \
    --enable-logging=stderr --v=0 --window-position=40,40 --window-size=$width,$height \
    ${extra[@]+"${extra[@]}"} --new-window "$url" > "$chromelog" 2>&1 &
  chrome=$!
  for ((i = 0; i < limit; i++)); do
    grep -q "LOADTEST_DONE\|LOADTEST_ERROR" "$chromelog" && break
    kill -0 $chrome 2>/dev/null || { log "  Chrome exited early"; break; }
    sleep 1
  done
  sleep 2; kill $chrome 2>/dev/null; sleep 2; kill -9 $chrome 2>/dev/null; rm -rf "$profile"
  python3 "$KIT/summarise.py" "$chromelog" "$name" | tee -a "$SUMMARY"
done

log "Done. Report: $SUMMARY"
