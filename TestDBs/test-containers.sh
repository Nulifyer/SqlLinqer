#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage: bash TestDBs/test-containers.sh [--engine docker|podman] [--sudo] [--host]

Build once, start disposable PostgreSQL, MySQL, and SQL Server databases,
then run every suite through the shared SqlLinqer.Tests executable.
Containers and their anonymous volumes are removed after success or failure.

Tests run in a .NET container on the databases' isolated network by default.
--host publishes random localhost ports and runs tests on the host instead.
--containerized is accepted as an explicit spelling of the default.
--sudo applies only to container commands; dotnet still runs as your user.

Image overrides: SQLLINQER_POSTGRES_IMAGE, SQLLINQER_MYSQL_IMAGE,
SQLLINQER_SQLSERVER_IMAGE, and SQLLINQER_RUNTIME_IMAGE.
HELP
}

runtime=""
use_sudo=false
containerized=true
while (($#)); do
    case "$1" in
        --engine)
            if (($# < 2)); then usage >&2; exit 2; fi
            runtime="$2"; shift 2 ;;
        --sudo) use_sudo=true; shift ;;
        --containerized) containerized=true; shift ;;
        --host) containerized=false; shift ;;
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
dotnet build "$task_repo/SqlLinqer.Tests/SqlLinqer.Tests.csproj" --nologo --verbosity quiet
umask 077
task_work=$(mktemp -d "${TMPDIR:-/tmp}/sqllinqer-db.XXXXXXXX")
run_id=$(basename -- "$task_work")
database_name="sqllinqer_integration_${run_id//[^[:alnum:]]/_}"
database_password="Aa1!$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
result_log=$(mktemp "${TMPDIR:-/tmp}/sqllinqer-tests.XXXXXXXX.log")
cleanup() {
    local task_exit=$?
    trap - EXIT
    for target in runner sqlserver mysql postgres; do
        local cidfile="$task_work/$target.cid"
        [[ -s "$cidfile" ]] || continue
        local container_id
        container_id=$(cat -- "$cidfile")
        if ((task_exit != 0)) && [[ "$target" != runner ]]; then
            "${engine[@]}" logs --tail 60 "$container_id" > "$result_log.$target.log" 2>&1 || true
            printf '%s container log: %s\n' "$target" "$result_log.$target.log"
        fi
        if ! "${engine[@]}" rm --force --volumes "$container_id" >/dev/null; then
            printf 'Could not remove test container %s.\n' "$container_id" >&2
            task_exit=1
        fi
    done
    rm -rf -- "$task_work"
    printf 'Full test results: %s\n' "$result_log"
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
cat > "$task_work/sqlserver.env" <<ENV
ACCEPT_EULA=Y
MSSQL_SA_PASSWORD=$database_password
MSSQL_MEMORY_LIMIT_MB=2048
ENV

printf 'Starting disposable PostgreSQL, MySQL, and SQL Server databases with %s.\n' "$runtime"
postgres_network=(--publish 127.0.0.1::5432)
mysql_network=(--publish 127.0.0.1::3306)
sqlserver_network=(--publish 127.0.0.1::1433)
if "$containerized"; then
    postgres_network=(--network none)
    mysql_network=(--network "container:$run_id-postgres")
    sqlserver_network=(--network "container:$run_id-postgres")
fi
"${engine[@]}" run --detach --cidfile "$task_work/postgres.cid" --name "$run_id-postgres" \
    --label io.sqllinqer.integration="$run_id" --env-file "$task_work/postgres.env" \
    "${postgres_network[@]}" "${SQLLINQER_POSTGRES_IMAGE:-docker.io/library/postgres:18-alpine}" >/dev/null
"${engine[@]}" run --detach --cidfile "$task_work/mysql.cid" --name "$run_id-mysql" \
    --label io.sqllinqer.integration="$run_id" --env-file "$task_work/mysql.env" \
    "${mysql_network[@]}" "${SQLLINQER_MYSQL_IMAGE:-docker.io/library/mysql:8.4}" >/dev/null
"${engine[@]}" run --detach --cidfile "$task_work/sqlserver.cid" --name "$run_id-sqlserver" \
    --label io.sqllinqer.integration="$run_id" --env-file "$task_work/sqlserver.env" \
    --memory 4g "${sqlserver_network[@]}" \
    "${SQLLINQER_SQLSERVER_IMAGE:-mcr.microsoft.com/mssql/server:2025-latest}" >/dev/null

sqlserver_id=$(cat -- "$task_work/sqlserver.cid")
sqlcmd_path=/opt/mssql-tools18/bin/sqlcmd
if ! "${engine[@]}" exec "$sqlserver_id" test -x "$sqlcmd_path"; then
    sqlcmd_path=/opt/mssql-tools/bin/sqlcmd
fi
sqlcmd() {
    "${engine[@]}" exec --env "SQLCMDPASSWORD=$database_password" "$sqlserver_id" \
        "$sqlcmd_path" -S tcp:127.0.0.1,1433 -U sa -C -b -l 3 -t 30 "$@"
}
ready=false
deadline=$((SECONDS + 120))
while ((SECONDS < deadline)); do
    if sqlcmd -Q 'SELECT 1' >/dev/null 2>&1; then ready=true; break; fi
    if [[ $("${engine[@]}" inspect --format '{{.State.Running}}' "$sqlserver_id") != true ]]; then break; fi
    sleep 1
done
if ! "$ready"; then printf 'SQL Server did not become ready.\n' >&2; exit 1; fi
sqlcmd -Q "CREATE DATABASE [$database_name]" >/dev/null

postgres_address=127.0.0.1:5432
mysql_address=127.0.0.1:3306
sqlserver_address=127.0.0.1:1433
if ! "$containerized"; then
    postgres_address=$("${engine[@]}" port "$(cat "$task_work/postgres.cid")" 5432/tcp)
    mysql_address=$("${engine[@]}" port "$(cat "$task_work/mysql.cid")" 3306/tcp)
    sqlserver_address=$("${engine[@]}" port "$sqlserver_id" 1433/tcp)
fi
export SQLLINQER_TEST_POSTGRES="Host=127.0.0.1;Port=${postgres_address##*:};Database=$database_name;Username=sqllinqer;Password=$database_password;Timeout=3;GSS Encryption Mode=Disable"
export SQLLINQER_TEST_MYSQL="Server=127.0.0.1;Port=${mysql_address##*:};Database=$database_name;User ID=sqllinqer;Password=$database_password;Connection Timeout=3;SslMode=Disabled;AllowPublicKeyRetrieval=True"
export SQLLINQER_TEST_SQLSERVER="Server=127.0.0.1,${sqlserver_address##*:};Database=$database_name;User ID=sa;Password=$database_password;Encrypt=true;TrustServerCertificate=true;Connect Timeout=5;ConnectRetryCount=0"
printf 'Running the full suite. Results: %s\n' "$result_log"
if "$containerized"; then
    cat > "$task_work/runner.env" <<ENV
SQLLINQER_TEST_POSTGRES=$SQLLINQER_TEST_POSTGRES
SQLLINQER_TEST_MYSQL=$SQLLINQER_TEST_MYSQL
SQLLINQER_TEST_SQLSERVER=$SQLLINQER_TEST_SQLSERVER
ENV
    "${engine[@]}" run --cidfile "$task_work/runner.cid" --name "$run_id-runner" \
        --label io.sqllinqer.integration="$run_id" --network "container:$run_id-postgres" \
        --user "$(id -u):$(id -g)" --env-file "$task_work/runner.env" \
        --volume "$task_repo/SqlLinqer.Tests/bin/Debug/net10.0:/app:ro" \
        "${SQLLINQER_RUNTIME_IMAGE:-mcr.microsoft.com/dotnet/runtime:10.0}" \
        dotnet /app/SqlLinqer.Tests.dll --all 2>&1 | tee "$result_log"
else
    dotnet run --no-build --project "$task_repo/SqlLinqer.Tests/SqlLinqer.Tests.csproj" -- --all 2>&1 | tee "$result_log"
fi
