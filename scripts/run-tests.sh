#!/usr/bin/env bash
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1
export AXORITH_TEST_MODE=1
export AXORITH_DEV=1
export AXORITH_TELEMETRY_DISABLED=1
export AXORITH_TELEMETRY_API_KEY='##E2E_DISABLED##'
export AXORITH_TELEMETRY_HOST=http://127.0.0.1:9
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project=All
suite=All
configuration=Release
filter=""
coverage=false
no_build=false
clean=false
verbose=minimal
while (( $# )); do
    case "$1" in
        --filter) filter="$2"; shift 2 ;;
        --project) project="$2"; shift 2 ;;
        --suite) suite="$2"; shift 2 ;;
        --configuration) configuration="$2"; shift 2 ;;
        --coverage) coverage=true; shift ;;
        --no-build) no_build=true; shift ;;
        --clean) clean=true; shift ;;
        --verbose) verbose=normal; shift ;;
        --open) echo 'Use the generated coverage XML or the PowerShell runner -OpenReport.' >&2; exit 1 ;;
        --help) echo 'run-tests.sh [--project Sdk|Core|Shared|Contracts|Host|Integrations|All] [--suite All|Unit|Integration] [--configuration Debug|Release] [--filter pattern] [--coverage] [--no-build] [--clean]'; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done
case "$project" in Sdk|Core|Shared|Contracts|Host|Integrations|All) ;; *) echo 'Invalid project' >&2; exit 1 ;; esac
case "$suite" in All|Unit|Integration) ;; *) echo 'Invalid suite' >&2; exit 1 ;; esac
case "$configuration" in Debug|Release) ;; *) echo 'Invalid configuration' >&2; exit 1 ;; esac
results="$root/build/$configuration"
mkdir -p "$results"
if $clean; then dotnet clean "$root/Axorith.sln" -c "$configuration" --verbosity quiet; fi
selected=0
for name in Sdk Core Shared Contracts Host Integrations; do
    [[ "$project" == All || "$project" == "$name" ]] || continue
    [[ "$suite" != Unit || "$name" != Integrations ]] || continue
    [[ "$suite" != Integration || "$name" == Integrations ]] || continue
    selected=$((selected + 1))
    effective_filter="$filter"
    if $coverage && [[ "$name" == Integrations ]]; then
        plain_args=(--project Integrations --configuration "$configuration")
        if $no_build; then plain_args+=(--no-build); fi
        bash "$root/scripts/run-tests.sh" "${plain_args[@]}"
        cp "$results/Axorith.Integrations.Tests.trx" "$results/Axorith.Integrations.Tests.uninstrumented.trx"
        excluded='FullyQualifiedName!~RealModulesWithPrivateDependenciesUnloadAfterHostShutdown'
        effective_filter="${filter:+($filter)&}$excluded"
    fi
    args=(test "$root/tests/Axorith.$name.Tests/Axorith.$name.Tests.csproj" -c "$configuration"
        --results-directory "$results" --logger "trx;LogFileName=Axorith.$name.Tests.trx" --verbosity "$verbose")
    if $no_build; then args+=(--no-build); fi
    if $coverage; then args+=("--collect:XPlat Code Coverage"); fi
    if [[ -n "$effective_filter" ]]; then args+=(--filter "$effective_filter"); fi
    trx="$results/Axorith.$name.Tests.trx"
    rm -f -- "$trx"
    dotnet "${args[@]}" -- RunConfiguration.TreatNoTestsAsError=true
    if [[ ! -s "$trx" ]] || grep -Eq '<Counters[^>]*executed="0"' "$trx"; then
        echo "$name produced no results or executed zero tests" >&2
        exit 1
    fi
done
if (( selected == 0 )); then echo 'Selected suite contains no test projects' >&2; exit 1; fi
