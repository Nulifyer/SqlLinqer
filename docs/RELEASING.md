# Publish a NuGet release

Publishing a GitHub release runs [.github/workflows/publish-nuget.yml](../.github/workflows/publish-nuget.yml).
The workflow tests the tagged source, builds `SqlLinqer`, and pushes the package to nuget.org.
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
   - Scope: **Push only new package versions**
   - Glob patterns and packages: `SqlLinqer`
3. Save the policy.

If another account publishes the package, grant that account package publishing rights and register the policy under that account.
Set the GitHub repository variable `NUGET_USER` to that account's nuget.org username, not its email address.
Without this variable, the workflow uses `Nulifyer`.
No permanent `NUGET_API_KEY` secret is required.

See [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) for the authentication details.

## Publish a version

1. Merge the workflow and the changes to release into `master`.
2. Create a GitHub release targeting the commit you want to distribute.
3. Give the release a new tag such as `v2.2.0`.
4. For a prerelease, use a tag such as `v2.2.0-beta.1` and select **Set as a pre-release**.
5. Publish the release.
6. Check the **Publish NuGet package** run in GitHub Actions.
7. After NuGet finishes validating and indexing the package, confirm the version on the [package page](https://www.nuget.org/packages/SqlLinqer).

The examples show tag formats. Choose the version according to the changes being released.
The workflow file must exist in the tagged commit. Adding it later does not update an existing tag.
Saving a draft release or pushing a tag alone does not publish a package.

The tag supplies the package version. For example, `v2.2.0` produces `SqlLinqer.2.2.0.nupkg`.
The workflow overrides the project's local development version during packing, so a separate version bump commit is unnecessary.
Tags must have three numeric components, without leading zeroes or build metadata such as `+build.1`.
The GitHub prerelease setting must match the presence of a prerelease suffix.

## Checks and retries

The build job runs `dotnet run --project SqlLinqer.Tests -c Release` for regression, SQLite, DuckDB, and Azure SQL local checks.
It does not run live Azure SQL or the PostgreSQL/MySQL container suite.
Run those checks separately when the release needs live database validation.
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
