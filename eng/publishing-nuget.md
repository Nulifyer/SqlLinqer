[Contributor documentation](README.md) · [Project README](../README.md)

# Publishing NuGet releases

Publishing a GitHub release runs [.github/workflows/publish-nuget.yml](../.github/workflows/publish-nuget.yml).
The workflow tests the tagged source, builds `SqlLinqer`, `SqlLinqer.Diagnostics` and `SqlLinqer.Extensions.DependencyInjection`, and pushes the packages to nuget.org.
It uses NuGet Trusted Publishing to obtain a temporary API key.

## Set up Trusted Publishing once

1. Sign in to [nuget.org](https://www.nuget.org/account/TrustedPublishing) as `Nulifyer`, the current `SqlLinqer` package owner.
2. Open **Trusted Publishing** and add a GitHub Actions policy with these values:
   - Policy name: `SqlLinqer GitHub releases`
   - Package owner: `Nulifyer`
   - CI/CD provider: `GitHub Actions`
   - Repository owner: `Nulifyer`
   - Repository: `SqlLinqer`
   - Workflow file: `publish-nuget.yml`, without the `.github/workflows/` prefix
   - Environment: leave empty, because the workflow does not use a GitHub environment
   - Scope: temporarily select **Push new packages and package versions** when publishing a package ID for the first time
   - Glob patterns and packages: enter `SqlLinqer`, `SqlLinqer.Diagnostics`, and `SqlLinqer.Extensions.DependencyInjection` on separate lines
3. Save the policy.
4. Publish the first versions, then edit the policy and switch to **Push only new package versions**. Keep all three package IDs authorized. Subsequent releases use this update-only scope. If first publication cannot proceed, restore the update-only scope before leaving the setup unfinished.

If another account publishes the package, grant that account package publishing rights and register the policy under that account.
Set the GitHub repository variable `NUGET_USER` to that account's nuget.org username, not its email address.
Without this variable, the workflow uses `Nulifyer`.
No permanent `NUGET_API_KEY` secret is required.

See [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) for the authentication details.

## Version naming policy

Use Semantic Versioning with three numeric components: `MAJOR.MINOR.PATCH`.
Increment the major version for breaking API changes, the minor version for compatible additions, and the patch version for compatible fixes.

Use `MAJOR.MINOR.PATCH-rc.N` for project prereleases. Start `N` at `1` for each target version and increment it for every new candidate.
Use the same version in the NuGet package and GitHub release title. Prefix only the Git tag with `v`.

For example, release candidates for the next minor version are `2.3.0-rc.1`, `2.3.0-rc.2`, and then stable `2.3.0`.
A later patch starts again at `2.3.1-rc.1`. Mark every `rc.N` GitHub release as a prerelease; mark the final version as stable.
A release candidate invites validation before the stable release and does not promise that every candidate will become stable.

Do not overwrite a package, move a published tag, or reuse a candidate number for different content.
Previously published labels, including `preview` labels, remain available under their original names. New prereleases follow the `rc.N` convention.
The project files carry the version `0.0.0-dev`. It marks a local development build and never changes between releases.
The publish workflow replaces it with the release tag's version through `-p:Version`.

## Pack a local build

To validate an application against unreleased source, pack all three projects into a local folder with the workflow's options:

```bash
for project in SqlLinqer/SQLLinqer.csproj SqlLinqer.Diagnostics/SqlLinqer.Diagnostics.csproj SqlLinqer.Extensions.DependencyInjection/SqlLinqer.Extensions.DependencyInjection.csproj; do
  dotnet pack "$project" -c Release --output artifacts/local \
    -p:Version=4.0.0-dev.1 -p:GeneratePackageOnBuild=false
done
```

Always pass `-p:GeneratePackageOnBuild=false`. The core project sets `GeneratePackageOnBuild`, and with that setting `dotnet pack` does not rebuild.
It then packs the existing `SQLLinqer.dll`, which has assembly version 0.0.0.0, while the other two packages reference 4.0.0.0.
An application that loads the dependency injection or diagnostics package then fails at startup with `FileNotFoundException` for `SQLLinqer, Version=4.0.0.0`.
A build, or a test that uses only the core package, does not show the fault.

Use a new `dev.N` number for each local build. NuGet caches a package by version and does not notice that a file changed.

Check which version the application resolved. If its restore cannot see the local folder, NuGet does not fail.
It reports warning NU1603 and uses the nearest higher version from nuget.org, which is a release candidate, because `dev` sorts below `rc`.
Read the resolved version in the application's `project.assets.json`, or treat NU1603 as an error during validation.
`artifacts` is ignored by Git.

## Publish a version

1. Merge the workflow and the changes to release into `master`.
2. Create a GitHub release targeting the commit you want to distribute.
3. Give the release a new tag such as `v2.2.0`.
4. For a prerelease, use a tag such as `v2.3.0-rc.1` and select **Set as a pre-release**.
5. Publish the release.
6. Check the **Publish NuGet packages** run in GitHub Actions.
7. After NuGet finishes validating and indexing the package, confirm the version on the [package page](https://www.nuget.org/packages/SqlLinqer).

The examples show tag formats. Choose the version according to the changes being released.
The workflow file must exist in the tagged commit. Adding it later does not update an existing tag.
Saving a draft release or pushing a tag alone does not publish a package.

The tag supplies the package version. For example, `v2.2.0` produces `SqlLinqer.2.2.0.nupkg`.
The workflow overrides the project's local development version during packing, so a separate version bump commit is unnecessary.
Tags must have three numeric components, without leading zeroes or build metadata such as `+build.1`.
The GitHub prerelease setting must match the presence of a prerelease suffix.

## Checks and retries

The reusable validation workflow must pass before packaging. It builds both compatibility programs, checks formatting, runs local suites and runner checks, and validates SQLite 3.38.
A separate job runs every suite against disposable PostgreSQL, MySQL, and SQL Server databases. Actual Azure SQL validation remains optional.
The same workflow runs for pull requests and pushes to master.
Library builds treat missing or invalid public XML documentation as errors. The NuGet package includes the XML file for C# editor tooltips.
The publish job receives only the package artifact and requests publishing credentials after the build job succeeds.

The package remains available as a workflow artifact for inspection and retry.
If authentication or upload fails, fix the policy or account configuration and rerun the failed job while the artifact remains available.
If the artifact has expired, rerun all jobs.
An already published version is skipped on retry. NuGet package versions cannot be overwritten.
To distribute different code, publish a new version with a new tag.

For local package inspection without publishing:

```sh
dotnet pack SqlLinqer/SQLLinqer.csproj -c Release --output artifacts \
  -p:Version=2.2.0 -p:GeneratePackageOnBuild=false -p:ContinuousIntegrationBuild=true
```

## Optional packages

The workflow packs all three packages with the release tag's version and publishes core before its integrations. Verify that the Trusted Publishing policy permits every package ID before the next release. A policy restricted to SqlLinqer does not authorize the optional package IDs. The repository cannot verify or edit that account-side policy locally.

The diagnostics and DI packages depend on the matching core release. Their presence in source or a local artifact does not mean a public version exists. Validate each package page after an actual release. Local consumer validation must restore package dependencies rather than use project references.
