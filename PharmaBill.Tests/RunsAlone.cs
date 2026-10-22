using Xunit;

namespace PharmaBill.Tests;

/// <summary>
/// Tests that must not share the process with others running at the same time: timings, and anything that reads
/// process-wide state another test can change (such as SQLite's connection pools). xUnit runs this collection on its
/// own, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RunsAlone
{
    public const string Name = "Runs alone";
}
