using Xunit;

namespace Underworld.Rules.Tests;

/// <summary>
/// Every rules test sets statics (Rng.Source, UWClass._RES, the object data buffers), so they all
/// run in this one collection, one at a time.
/// </summary>
[CollectionDefinition("Rules", DisableParallelization = true)]
public class RulesCollection { }
