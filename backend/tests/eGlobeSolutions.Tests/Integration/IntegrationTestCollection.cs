using Xunit;

namespace eGlobeSolutions.Tests.Integration;

/// <summary>
/// All integration test classes share one real SQL Server database (see
/// CustomWebApplicationFactory), not an isolated per-test store. Put them
/// in one xUnit collection so classes don't run in parallel against each
/// other, individual [Fact]s within a class still run in parallel-safe
/// isolation via unique GUIDs in test data, but cross-class races on shared
/// seeded rows (the SuperAdmin account, in particular) are what this avoids.
/// </summary>
[CollectionDefinition("Integration", DisableParallelization = true)]
public class IntegrationTestCollection : ICollectionFixture<CustomWebApplicationFactory>
{
}
