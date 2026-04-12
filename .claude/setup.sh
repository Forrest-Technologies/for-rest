#!/usr/bin/env bash
# For-Rest development environment bootstrap for Claude Code web sessions.
# Run once at session start:  bash .claude/setup.sh
#
# What it does:
#   1. Installs .NET 10 SDK from Ubuntu apt archives (bypasses blocked
#      CDN hosts like dotnetcli.azureedge.net / builds.dotnet.microsoft.com
#      by downloading .deb packages directly from archive.ubuntu.com).
#   2. Installs the MAUI workload so ForRest.Maui.Tests can build.
#   3. Patches global.json to accept the apt-provided SDK version
#      (the repo pins a 10.0.2xx preview that apt doesn't carry).
#   4. Smoke-builds both test projects and runs the cross-platform
#      test suite to prove the toolchain works.
#
# Safe to re-run — skips steps that already completed.

set -euo pipefail

DOTNET_SDK_VERSION_INSTALLED=""
GLOBAL_JSON="global.json"
GLOBAL_JSON_BAK="global.json.setup-bak"

log() { printf '\033[1;36m=> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m=> %s\033[0m\n' "$*"; }
err()  { printf '\033[1;31m=> %s\033[0m\n' "$*" >&2; }

# ── Step 1: Install .NET 10 SDK ──────────────────────────────────────

install_dotnet_sdk() {
    if command -v dotnet &>/dev/null; then
        DOTNET_SDK_VERSION_INSTALLED="$(dotnet --list-sdks 2>/dev/null | grep '^10\.' | head -1 | awk '{print $1}')"
        if [[ -n "$DOTNET_SDK_VERSION_INSTALLED" ]]; then
            log ".NET SDK $DOTNET_SDK_VERSION_INSTALLED already installed — skipping."
            return 0
        fi
    fi

    log "Installing .NET 10 SDK from Ubuntu archives..."

    # The egress proxy blocks dotnetcli.azureedge.net and
    # builds.dotnet.microsoft.com, but archive.ubuntu.com is allowed.
    # Grab the package URIs from apt, download them with curl, and
    # install via dpkg to sidestep the proxy-blocked apt fetch.
    local deb_dir="/tmp/dotnet-debs-setup"
    mkdir -p "$deb_dir"

    local urls
    urls="$(apt-get install -y --print-uris --no-install-recommends dotnet-sdk-10.0 2>/dev/null \
        | grep -oE "http://archive\.ubuntu\.com/[^']+" || true)"

    if [[ -z "$urls" ]]; then
        # Fallback: try a direct apt install (works when the proxy
        # allows archive.ubuntu.com fetches end-to-end).
        log "No URI extraction — trying direct apt install..."
        sudo apt-get install -y --no-install-recommends dotnet-sdk-10.0
    else
        log "Downloading $(echo "$urls" | wc -l) packages..."
        echo "$urls" | xargs -P 4 -I{} curl -sSL -o "$deb_dir/$(basename {})" "{}"
        log "Installing packages via dpkg..."
        sudo dpkg -i "$deb_dir"/*.deb 2>&1 | tail -5
        rm -rf "$deb_dir"
    fi

    if ! command -v dotnet &>/dev/null; then
        err "dotnet not found after install. Check apt output above."
        return 1
    fi

    DOTNET_SDK_VERSION_INSTALLED="$(dotnet --list-sdks 2>/dev/null | grep '^10\.' | head -1 | awk '{print $1}')"
    log "Installed .NET SDK $DOTNET_SDK_VERSION_INSTALLED"
}

# ── Step 2: Install MAUI workload ────────────────────────────────────

install_maui_workload() {
    if dotnet workload list 2>/dev/null | grep -q 'maui'; then
        log "MAUI workload already installed — skipping."
        return 0
    fi

    log "Installing MAUI workload..."
    dotnet workload install maui-tizen 2>&1 | tail -10
    log "MAUI workload installed."
}

# ── Step 3: Patch global.json ────────────────────────────────────────

patch_global_json() {
    if [[ ! -f "$GLOBAL_JSON" ]]; then
        warn "No global.json found — skipping SDK pin adjustment."
        return 0
    fi

    local pinned_version
    pinned_version="$(grep -oP '"version"\s*:\s*"\K[^"]+' "$GLOBAL_JSON" | head -1)"

    if [[ -z "$pinned_version" ]]; then
        warn "Could not parse pinned SDK version from global.json."
        return 0
    fi

    # Extract the feature band (e.g. 10.0.200 from 10.0.200-preview.0.xxx)
    local pinned_feature
    pinned_feature="$(echo "$pinned_version" | grep -oP '^\d+\.\d+\.\d+')"
    local installed_feature
    installed_feature="$(echo "$DOTNET_SDK_VERSION_INSTALLED" | grep -oP '^\d+\.\d+\.\d+')"

    if [[ "$pinned_feature" == "$installed_feature" ]]; then
        log "Installed SDK $DOTNET_SDK_VERSION_INSTALLED matches global.json pin — no patch needed."
        return 0
    fi

    log "Patching global.json: $pinned_version -> $DOTNET_SDK_VERSION_INSTALLED (rollForward: latestPatch)"
    cp "$GLOBAL_JSON" "$GLOBAL_JSON_BAK"
    cat > "$GLOBAL_JSON" <<JSONEOF
{
  "sdk": {
    "version": "$DOTNET_SDK_VERSION_INSTALLED",
    "rollForward": "latestPatch"
  }
}
JSONEOF
    log "Original global.json backed up to $GLOBAL_JSON_BAK"
    log "IMPORTANT: Do NOT commit global.json — restore before pushing:"
    log "  mv $GLOBAL_JSON_BAK $GLOBAL_JSON"
}

# ── Step 4: Smoke build + test ───────────────────────────────────────

smoke_test() {
    log "Building ForRest.Tests..."
    dotnet build tests/ForRest.Tests/ForRest.Tests.csproj -v quiet 2>&1 | tail -3

    log "Running ForRest.Tests..."
    dotnet test tests/ForRest.Tests/ForRest.Tests.csproj --no-build --nologo -v minimal 2>&1 | tail -5

    log "Building ForRest.Maui.Tests (cross-compile for Windows target)..."
    dotnet build tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj -p:EnableWindowsTargeting=true -v quiet 2>&1 | tail -3

    log "Running ForRest.Maui.Tests (focused — full run hits WinRT finalizer crash on Linux)..."
    dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj \
        --no-build -p:EnableWindowsTargeting=true --nologo -v minimal \
        --filter "FullyQualifiedName~SecretMaskingServiceTests|FullyQualifiedName~AiDebugSummaryFormatterTests|FullyQualifiedName~MonacoEditorSurfaceSourceTests|FullyQualifiedName~ResponsePaneCopyFormatterTests|FullyQualifiedName~ThemeConfigParserTests" \
        2>&1 | tail -5

    log "Done. Environment is ready for development."
}

# ── Main ─────────────────────────────────────────────────────────────

cd "$(dirname "$0")/.."
log "Setting up For-Rest dev environment in $(pwd)"

install_dotnet_sdk
install_maui_workload
patch_global_json
smoke_test

echo ""
log "Quick reference:"
log "  Build all tests:    dotnet build tests/ForRest.Tests/ForRest.Tests.csproj"
log "  Run all tests:      dotnet test tests/ForRest.Tests/ForRest.Tests.csproj --nologo"
log "  Build Maui tests:   dotnet build tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj -p:EnableWindowsTargeting=true"
log "  Run Maui tests:     dotnet test tests/ForRest.Maui.Tests/ForRest.Maui.Tests.csproj --no-build -p:EnableWindowsTargeting=true --nologo --filter 'FullyQualifiedName~YourTestClass'"
log "  Restore global.json: mv $GLOBAL_JSON_BAK $GLOBAL_JSON"
