#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage: bash TestDBs/test-containers.sh [--engine docker|podman] [--sudo] [--containerized]

Start disposable PostgreSQL and MySQL containers, run the real-database
integration checks, then remove the containers and their anonymous volumes.
Ports are assigned automatically and exposed only on 127.0.0.1.

--sudo applies only to container commands; dotnet still runs as your user.
--containerized runs the built tests in a .NET runtime container alongside the
databases, without published ports or host-to-container connections.
Image overrides: SQLLINQER_POSTGRES_IMAGE, SQLLINQER_MYSQL_IMAGE,
and SQLLINQER_RUNTIME_IMAGE.
HELP
}

runtime=""
use_sudo=false
containerized=false
while (($#)); do
    case "$1" in
        --engine)
            if (($# < 2)); then usage >&2; exit 2; fi
            runtime="$2"; shift 2 ;;
        --sudo) use_sudo=true; shift ;;
        --containerized) containerized=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
done
if [[ -z "$runtime" ]]; then
    if command -v docker >/dev/null 2>&1; then runtime=docker
    elif command -v podman >/dev/null 2>&1; then runtime=podman
    else printf 'Install Docker or Podman before running these tests.\n' >&2; exit 1
    fi
fi
if [[ "$runtime" != docker && "$runtime" != podman ]]; then usage >&2; exit 2; fi
command -v "$runtime" >/dev/null
command -v dotnet >/dev/null
if ((EUID == 0)); then
    printf 'Run as your normal user with --sudo instead of running the whole script as root.\n' >&2
    exit 2
fi
engine=("$runtime")
if "$use_sudo"; then engine=(sudo "$runtime"); fi
if ! "${engine[@]}" info >/dev/null; then
    printf 'Cannot access %s. Use --sudo in your terminal, or select an accessible container engine.\n' "$runtime" >&2
    exit 1
fi

task_repo=$(realpath -- "$(dirname -- "${BASH_SOURCE[0]}")/..")
dotnet build "$task_repo/SqlLinqer.IntegrationTests/SqlLinqer.IntegrationTests.csproj" --nologo --verbosity quiet
umask 077
task_work=$(mktemp -d "${TMPDIR:-/tmp}/sqllinqer-db.XXXXXXXX")
run_id=$(basename -- "$task_work")
database_name="sqllinqer_integration_${run_id//[^[:alnum:]]/_}"
database_password=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
result_log=$(mktemp "${TMPDIR:-/tmp}/sqllinqer-integration.XXXXXXXX.log")
cleanup() {
    local task_exit=$?
    trap - EXIT
    for cidfile in "$task_work/runner.cid" "$task_work/mysql.cid" "$task_work/postgres.cid"; do
        [[ -s "$cidfile" ]] || continue
        local container_id
        container_id=$(cat -- "$cidfile")
        if ((task_exit != 0)); then
            "${engine[@]}" logs --tail 60 "$container_id" >&2 || true
        fi
        if ! "${engine[@]}" rm --force --volumes "$container_id" >/dev/null; then
            printf 'Could not remove test container %s.\n' "$container_id" >&2
            task_exit=1
        fi
    done
    rm -rf -- "$task_work"
    printf 'Integration results: %s\n' "$result_log"
    exit "$task_exit"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

cat > "$task_work/postgres.env" <<ENV
POSTGRES_DB=$database_name
POSTGRES_USER=sqllinqer
POSTGRES_PASSWORD=$database_password
ENV
cat > "$task_work/mysql.env" <<ENV
MYSQL_DATABASE=$database_name
MYSQL_USER=sqllinqer
MYSQL_PASSWORD=$database_password
MYSQL_ROOT_PASSWORD=$database_password
ENV

printf 'Starting disposable PostgreSQL and MySQL containers with %s.\n' "$runtime"
postgres_network=(--publish 127.0.0.1::5432)
mysql_network=(--publish 127.0.0.1::3306)
if "$containerized"; then
    postgres_network=(--network none)
    mysql_network=(--network "container:$run_id-postgres")
fi
"${engine[@]}" run --detach --cidfile "$task_work/postgres.cid" --name "$run_id-postgres" \
    --label io.sqllinqer.integration="$run_id" --env-file "$task_work/postgres.env" \
    "${postgres_network[@]}" "${SQLLINQER_POSTGRES_IMAGE:-docker.io/library/postgres:18-alpine}" >/dev/null
"${engine[@]}" run --detach --cidfile "$task_work/mysql.cid" --name "$run_id-mysql" \
    --label io.sqllinqer.integration="$run_id" --env-file "$task_work/mysql.env" \
    "${mysql_network[@]}" "${SQLLINQER_MYSQL_IMAGE:-docker.io/library/mysql:8.4}" >/dev/null

postgres_address=127.0.0.1:5432
mysql_address=127.0.0.1:3306
if ! "$containerized"; then
    postgres_address=$("${engine[@]}" port "$(cat "$task_work/postgres.cid")" 5432/tcp)
    mysql_address=$("${engine[@]}" port "$(cat "$task_work/mysql.cid")" 3306/tcp)
fi
export SQLLINQER_TEST_POSTGRES="Host=127.0.0.1;Port=${postgres_address##*:};Database=$database_name;Username=sqllinqer;Password=$database_password;Timeout=3;GSS Encryption Mode=Disable"
export SQLLINQER_TEST_MYSQL="Server=127.0.0.1;Port=${mysql_address##*:};Database=$database_name;User ID=sqllinqer;Password=$database_password;Connection Timeout=3;SslMode=Disabled;AllowPublicKeyRetrieval=True"
printf 'Integration results will be saved to %s\n' "$result_log"
if "$containerized"; then
    cat > "$task_work/runner.env" <<ENV
SQLLINQER_TEST_POSTGRES=$SQLLINQER_TEST_POSTGRES
SQLLINQER_TEST_MYSQL=$SQLLINQER_TEST_MYSQL
ENV
    "${engine[@]}" run --cidfile "$task_work/runner.cid" --name "$run_id-runner" \
        --label io.sqllinqer.integration="$run_id" --network "container:$run_id-postgres" \
        --user "$(id -u):$(id -g)" --env-file "$task_work/runner.env" \
        --volume "$task_repo/SqlLinqer.IntegrationTests/bin/Debug/net10.0:/app:ro" \
        "${SQLLINQER_RUNTIME_IMAGE:-mcr.microsoft.com/dotnet/runtime:10.0}" \
        dotnet /app/SqlLinqer.IntegrationTests.dll 2>&1 | tee "$result_log"
else
    dotnet run --no-build --project "$task_repo/SqlLinqer.IntegrationTests/SqlLinqer.IntegrationTests.csproj" 2>&1 | tee "$result_log"
fi
