#!/bin/bash
#=============================================================================
# UnityProjectInspector — One-Click Demo for macOS
#
#   Demo.command can be double-clicked in Finder or run from a terminal.
#   It automatically locates the repository root, builds the CLI (if needed),
#   and runs two static inspections against the bundled MinimalUnityProject
#   fixture:
#
#     [1] Static Inspection  (scene-check.json)  → expected exit 0
#     [2] Failure Detection  (static-fail.json)  → expected exit 1
#
#   No Unity Editor is required.  The demo reports a summary and waits for
#   the user to press Enter before closing the terminal window.
#=============================================================================

set -euo pipefail

# ── Locate the repository root (directory containing Demo.command) ──────
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"
unset SCRIPT_DIR

# Verify we are inside the expected repo structure
if [ ! -f "$REPO_ROOT/UnityProjectInspector.slnx" ]; then
    echo "ERROR: Repository root not found."
    echo "Expected UnityProjectInspector.slnx at: $REPO_ROOT"
    echo "Please run Demo.command from the repository root."
    exit 2
fi

# ── Paths used in this demo ──────────────────────────────────────────────
CLI_PROJECT="$REPO_ROOT/src/UnityProjectInspector.Cli"
FIXTURE_DIR="$REPO_ROOT/tests/fixtures/MinimalUnityProject"
ASSIGNMENT_SCENE_CHECK="$REPO_ROOT/examples/assignments/scene-check.json"
ASSIGNMENT_STATIC_FAIL="$REPO_ROOT/examples/assignments/static-fail.json"

# ── Terminal formatting helpers ──────────────────────────────────────────
BOLD="$(tput bold 2>/dev/null || echo '')"
NORMAL="$(tput sgr0 2>/dev/null || echo '')"
GREEN="$(tput setaf 2 2>/dev/null || echo '')"
RED="$(tput setaf 1 2>/dev/null || echo '')"
YELLOW="$(tput setaf 3 2>/dev/null || echo '')"
CYAN="$(tput setaf 6 2>/dev/null || echo '')"
RESET="$(tput sgr0 2>/dev/null || echo '')"

log_info()  { printf "  %s·%s %s\n" "$CYAN" "$RESET" "$1"; }
log_ok()    { printf "  %s✓%s %s\n" "$GREEN" "$RESET" "$1"; }
log_fail()  { printf "  %s✗%s %s\n" "$RED" "$RESET" "$1"; }
log_skip()  { printf "  %s−%s %s\n" "$YELLOW" "$RESET" "$1"; }

# ── Header ──────────────────────────────────────────────────────────────
echo ""
echo "${BOLD}══════════════════════════════════════════════════════════${NORMAL}"
echo "${BOLD}  UnityProjectInspector 演示${NORMAL}"
echo "${BOLD}══════════════════════════════════════════════════════════${NORMAL}"
echo ""
echo "  Repository:  $REPO_ROOT"
echo "  CLI:         $CLI_PROJECT"
echo "  Fixture:     $FIXTURE_DIR"
echo ""

# ── Check dotnet SDK ────────────────────────────────────────────────────
if ! command -v dotnet >/dev/null 2>&1; then
    echo "${RED}ERROR: dotnet CLI not found.${RESET}"
    echo ""
    echo "  Please install the .NET SDK 10.0 from:"
    echo "    https://dotnet.microsoft.com/download"
    echo ""
    echo "  After installation, re-run Demo.command."
    exit 1
fi

# ── Build ────────────────────────────────────────────────────────────────
log_info "Building CLI..."
if dotnet build "$CLI_PROJECT" -v q > /dev/null 2>&1; then
    log_ok "Build succeeded"
else
    dotnet build "$CLI_PROJECT" --no-restore -v minimal 2>&1 || true
    echo ""
    echo "${RED}Build failed. See above for details.${RESET}"
    exit 1
fi

# ── Run demo steps ──────────────────────────────────────────────────────

PASS_COUNT=0
EXPECTED_FAIL_COUNT=0
UNEXPECTED_FAIL_COUNT=0

run_case() {
    local step="$1"
    local label="$2"
    local assignment_path="$3"
    local expected_exit="$4"
    local is_expected_fail="$5"   # "yes" or "no"

    echo ""
    echo "  ${BOLD}──────────────────────────────────────────────${NORMAL}"
    printf "  ${BOLD}[%s] %s${NORMAL}\n" "$step" "$label"
    echo "  ${BOLD}──────────────────────────────────────────────${NORMAL}"
    echo ""

    # Print the CLI command (shortened)
    local assignment_name
    assignment_name="$(basename "$assignment_path")"
    printf "  %sAssignment:${RESET} %s\n" "$CYAN" "$assignment_name"
    echo ""

    # Execute the CLI directly (Chinese output)
    local exit_code=0
    local output
    output=$(dotnet run --no-build --project "$CLI_PROJECT" -- inspect \
        --project "$FIXTURE_DIR" \
        --assignment "$assignment_path" \
        --format chinese 2>&1) || exit_code=$?

    echo "$output"

    echo ""

    if [ "$exit_code" -eq "$expected_exit" ]; then
        if [ "$is_expected_fail" = "yes" ]; then
            log_ok "Result: EXPECTED FAIL (exit $exit_code)"
            EXPECTED_FAIL_COUNT=$((EXPECTED_FAIL_COUNT + 1))
        else
            log_ok "Result: PASS (exit $exit_code)"
            PASS_COUNT=$((PASS_COUNT + 1))
        fi
    else
        log_fail "Result: UNEXPECTED (expected exit $expected_exit, got $exit_code)"
        UNEXPECTED_FAIL_COUNT=$((UNEXPECTED_FAIL_COUNT + 1))
    fi
}

# Case 1: Static Inspection — expected PASS (exit 0)
run_case "1/2" "Static Inspection" \
    "$ASSIGNMENT_SCENE_CHECK" 0 "no"

# Case 2: Failure Detection — expected FAIL (exit 1)
run_case "2/2" "Failure Detection" \
    "$ASSIGNMENT_STATIC_FAIL" 1 "yes"

# ── Summary ─────────────────────────────────────────────────────────────
echo ""
echo "${BOLD}══════════════════════════════════════════════════════════${NORMAL}"
echo "${BOLD}  演示完成${NORMAL}"
echo "${BOLD}══════════════════════════════════════════════════════════${NORMAL}"
echo ""
printf "  %s通过:${RESET}          %d\n" "$GREEN" "$PASS_COUNT"
printf "  %s预期失败:${RESET} %d\n" "$YELLOW" "$EXPECTED_FAIL_COUNT"
printf "  %s意外失败:%s %d\n" "$([ "$UNEXPECTED_FAIL_COUNT" -gt 0 ] && echo "$RED" || echo "$GREEN")" "$RESET" "$UNEXPECTED_FAIL_COUNT"
echo ""

if [ "$UNEXPECTED_FAIL_COUNT" -eq 0 ]; then
    echo "  ${GREEN}${BOLD}演示全部通过。${NORMAL}${RESET}"
    FINAL_EXIT=0
else
    echo "  ${RED}${BOLD}演示包含意外的失败。${NORMAL}${RESET}"
    FINAL_EXIT=1
fi

echo ""
printf "  Press ${BOLD}Enter${NORMAL} to close this window..."
read -r _
exit "$FINAL_EXIT"