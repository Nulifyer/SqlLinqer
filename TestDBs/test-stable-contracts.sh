#!/usr/bin/env bash
set -euo pipefail

# Runs the same wire-format fixtures against the stable source and this checkout.
# Provider SQL execution remains the responsibility of test-containers.sh.
task_repo=$(realpath -- "$(dirname -- "${BASH_SOURCE[0]}")/..")
task_work=$(mktemp -d "${TMPDIR:-/tmp}/sqllinqer-stable-contracts.XXXXXXXX")
stable_commit=$(git -C "$task_repo" rev-parse 'v2.1.0^{commit}')
mkdir "$task_work/stable" "$task_work/harness"
git -C "$task_repo" archive "$stable_commit" SqlLinqer | tar -x -C "$task_work/stable"
printf '%s\n' "$stable_commit" > "$task_work/stable-commit.txt"
cat > "$task_work/harness/Contracts.csproj" <<'PROJECT'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);CS0649</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(ContractLibrary)" />
    <Compile Include="$(ContractRepo)/SqlLinqer.Tests/Regression/StableContractChecks.cs" Link="StableContractChecks.cs" />
    <Compile Include="$(ContractRepo)/SqlLinqer.Tests/Regression/FakeDatabase.cs" Link="FakeDatabase.cs" />
    <Compile Include="$(ContractRepo)/SqlLinqer.Tests/Regression/AsyncTrackingReader.cs" Link="AsyncTrackingReader.cs" />
  </ItemGroup>
</Project>
PROJECT
cat > "$task_work/harness/Program.cs" <<'PROGRAM'
using SqlLinqer.Tests.Regression;
int failed = 0;
StableContractChecks.Run((name, run) =>
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
});
return failed == 0 ? 0 : 1;
PROGRAM
failed=false
for version in stable current; do
    library="$task_repo/SqlLinqer/SQLLinqer.csproj"
    if [[ "$version" == stable ]]; then library="$task_work/stable/SqlLinqer/SQLLinqer.csproj"; fi
    if ! dotnet run --project "$task_work/harness/Contracts.csproj" \
        -p:ContractLibrary="$library" -p:ContractRepo="$task_repo" \
        > "$task_work/$version.log" 2>&1; then failed=true; fi
    rg '^(PASS|FAIL) ' "$task_work/$version.log" > "$task_work/$version.results" || true
done
sha256sum "$task_repo/SqlLinqer.Tests/Regression/StableContractChecks.cs" "$task_work/"*.log > "$task_work/hashes.txt"
if ! diff -u "$task_work/stable.results" "$task_work/current.results"; then failed=true; fi
printf 'Stable contract evidence: %s\n' "$task_work"
if "$failed"; then
    tail -n 40 "$task_work/stable.log" "$task_work/current.log"
    exit 1
fi
printf 'All %s shared fixtures pass against 2.1.0 and the working library.\n' "$(wc -l < "$task_work/current.results")"
