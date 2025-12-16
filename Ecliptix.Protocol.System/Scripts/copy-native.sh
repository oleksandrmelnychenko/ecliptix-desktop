#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
RID="${RID:-osx-arm64}"
BASE_NAME="${BASE_NAME:-ecliptix_protocol}"

case "${RID}" in
  win-*) EXT=".dll" ;;
  osx-*) EXT=".dylib" ;;
  *) EXT=".so" ;;
esac

# Candidate roots in priority order
declare -a ROOTS
if [[ -n "${NATIVE_SRC_ROOT:-}" ]]; then
  ROOTS+=("${NATIVE_SRC_ROOT}")
fi
ROOTS+=(
  "/Users/oleksandrmelnychenko/CLionProjects/Ecliptix.Protocol.System/build/${RID}"
  "/Users/oleksandrmelnychenko/CLionProjects/Ecliptix.Protocol.System/build"
  "/Users/oleksandrmelnychenko/CLionProjects/Ecliptix.Protocol.System/cmake-build-debug"
  "${REPO_ROOT}/../Ecliptix.Protocol.System/build/${RID}"
  "${REPO_ROOT}/../Ecliptix.Protocol.System/build"
)

SRC=""
for root in "${ROOTS[@]}"; do
  [[ -d "${root}" ]] || continue
  for name in "${BASE_NAME}" "lib${BASE_NAME}"; do
    candidate="${root}/${name}${EXT}"
    if [[ -f "${candidate}" ]]; then
      SRC="${candidate}"
      break 2
    fi
  done
done

DST_DIR="${REPO_ROOT}/native/${RID}"
mkdir -p "${DST_DIR}"

if [[ -z "${SRC}" ]]; then
  echo "Source native lib not found. Checked:" >&2
  printf '  - %s\n' "${ROOTS[@]}" >&2
  echo "Tip: build the shared library in the C++ repo (cmake -DBUILD_SHARED_LIBS=ON ...) or set NATIVE_SRC_ROOT=/path/to/output" >&2
  exit 1
fi

cp -f "${SRC}" "${DST_DIR}/"
echo "Copied ${SRC} -> ${DST_DIR}/"
