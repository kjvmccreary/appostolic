global using Xunit;
global using FluentAssertions;

// Disable test parallelization to prevent WebApplicationFactory/TestServer disposal races
// and background hosted service interference across concurrently running tests.
// Many tests spin up in-memory hosts with hosted services (workers/dispatchers),
// which can conflict when run in parallel in the same process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
