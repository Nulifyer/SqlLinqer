# Working on SqlLinqer

Maintainer material lives here. Application setup, how-to guides, and examples belong in [`docs/`](../docs/README.md); the root and package READMEs introduce the project's value and direct users to those guides.

- [Build and test](build-and-test.md).
- [Query and execution architecture](implementation-notes.md) and [pipeline details](query-pipeline.md).
- [Diagnostics contracts](diagnostics-implementation.md) and [acceptance scenarios](diagnostics-flows.md).
- [Performance methodology and remaining work](performance.md).
- [Publish packages](publishing-nuget.md).

Keep generated traces, database fixtures, benchmark outputs, and package/build artifacts out of source control. Use ignored `TestResults/` for local recordings. Retain only the evidence needed to understand a decision; archive historical experiments outside the repository when they are no longer executable against the supported code.
