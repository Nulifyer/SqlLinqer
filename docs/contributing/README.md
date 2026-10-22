[User documentation](../README.md) · [Project README](../../README.md)

# Contributor documentation

These guides cover changes to SqlLinqer itself.
To use the NuGet package in an application, start with the [user guides](../README.md).

- [Build and test](build-and-test.md): build the library and follow source organization conventions.
- [Test suites](../../SqlLinqer.Tests/README.md): run local checks and disposable database containers.
- [SQLite integration checks](../../SqlLinqer.Tests/Sqlite/README.md): verify SQLite behavior and minimum engine support.
- [SQL Server and Azure SQL integration checks](../../SqlLinqer.Tests/SqlServer/README.md): run procedure, TVP, and provider checks.
- [Scale checks](../../SqlLinqer.Tests/Scale/README.md): test larger datasets and collect measurements.
- [Streaming benchmarks](../../SqlLinqer.Streaming.Benchmarks/README.md): measure memory and mapping allocations.
- [Library comparisons](../../SqlLinqer.Comparisons/README.md): validate and measure equivalent workloads across five .NET data libraries.
- [Implementation notes](implementation-notes.md): query capture, SQL generation, and custom connector hooks.
- [Publish a NuGet release](publishing-nuget.md): prepare, publish, and verify a package.
