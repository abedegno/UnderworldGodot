using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// One gameplay scenario, run inside the real game: the runner boots Underworld.tscn, starts a
/// new game, seeds Rng.Source with Seed, then calls Setup, Act and Assert in that order.
///
/// Setup puts the game into the state the scenario needs and records what Assert compares
/// against. Act does the thing, over real physics frames (ScenarioContext.Frames). Assert
/// returns null for a pass or the reason for a failure. Assert both the precondition (that
/// Setup really got the game where it meant to) and the effect, so a scenario cannot pass
/// because its action never happened. Cite the original's code for every expected value.
/// </summary>
public abstract class Scenario
{
    /// <summary>The name used on the command line and in the verdict line, e.g. "clock-runs".</summary>
    public abstract string Name { get; }

    /// <summary>The games this scenario applies to; for any other the runner prints SKIP.</summary>
    public virtual string[] Games => new[] { "uw1", "uw2" };

    /// <summary>Where the expected behaviour comes from in the original (file:line).</summary>
    public abstract string Cites { get; }

    /// <summary>The seed for the BorlandRandSource the runner installs as Rng.Source.</summary>
    public virtual uint Seed => 1;

    public abstract Task Setup(ScenarioContext c);
    public abstract Task Act(ScenarioContext c);

    /// <summary>Null for a pass, otherwise why it failed.</summary>
    public abstract string Assert(ScenarioContext c);
}

/// <summary>What a scenario can use: the game being run and a way to let physics frames pass.</summary>
public sealed class ScenarioContext
{
    private readonly Node _host;

    public ScenarioContext(Node host, string game, Node gameRoot)
    {
        _host = host;
        Game = game;
        GameRoot = gameRoot;
    }

    /// <summary>"uw1" or "uw2".</summary>
    public string Game { get; }

    /// <summary>The instantiated Underworld.tscn root.</summary>
    public Node GameRoot { get; }

    /// <summary>Waits for n physics frames. Under --fixed-fps 60 each is 1/60 s of game time.</summary>
    public async Task Frames(int n)
    {
        var tree = _host.GetTree();
        for (int i = 0; i < n; i++)
        {
            await _host.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Prints an indented note, which appears above the scenario's verdict line, for diagnosis.</summary>
    public void Note(string message) => GD.Print("    note: " + message);
}
