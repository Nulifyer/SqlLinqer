# SqlLinqer.Extensions.DependencyInjection

Register reusable connectors with `AddSqlLinqer` and `AddKeyedSqlLinqer`. Each registration creates a singleton connector; its factory must return a fresh provider connection for each operation. Factory dependencies must have singleton lifetimes.

Available on nuget.org starting with `2.3.0-rc.2`. Use the matching core package version. See the [repository documentation](https://github.com/Nulifyer/SqlLinqer/tree/master/docs) and runnable `samples/SqlLinqer.MinimalApi` example for usage and lifetime details.
