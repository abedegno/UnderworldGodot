using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Finishing the Q*bert pyramid shows its moongate, and it stays (UW2). The original's do_qbert
/// (hack trap 32, uw2/src/event/WORLDEV.C:1880-2010) keeps the colour sequence in vars 100 on,
/// the "not complete" flag in var 107 and the last tile stepped on in var 108 (seq, done = seq +
/// 7, last_tile = seq + 8, WORLDEV.C:1892-1895). Stepping on a pyramid tile (owner 0x3F) moves its
/// floor to the next colour of the sequence (WORLDEV.C:1914-1928); when the 5 by 5 pyramid at
/// (0x31 - i, 0x33 + j) is then all one colour, the walls take it, var 107 is cleared, object 0x3CC
/// is raised to z 0x60, object 0x3CD becomes a teleport to colour * 6 + 4 (quality 4), and the
/// moongate 0x3CE gets link LINK_SPECIAL | gate_links[colour] and is made visible
/// (WORLDEV.C:1929-1961). It is hidden again only when the pyramid is not complete and var 107
/// was clear (WORLDEV.C:1962-1968).
///
/// The port's StepOnPyramidTile reads its flag from var 114, which nothing writes
/// (src/traps/a_hack_trap_qbert.cs:100), so it hides the gate (and lowers 0x3CC) right after
/// showing it (a_hack_trap_qbert.cs:269).
///
/// The scenario goes to the pyramid top on level 69 (0x45, where the colour hack traps send the
/// player, WORLDEV.C:1980), sets the sequence to colours 6 then 2, every pyramid floor to 2 but
/// the top's to 6, var 107 to 1 and the last-tile var 108 (16 bits) to 0, and fires
/// the pyramid trap (owner 0x3F) through a_hack_trap_qbert.Activate, as the top tile's trigger does.
/// The top turns 2, completing the pyramid in colour 2.
///
/// Var 104 (seq[4]) is set to 2 as well, so the finished colour is the sequence's fifth and the
/// moongate 0x29A is shown too (WORLDEV.C:1952-1956); the port tested var 108 there. The trap is
/// then fired a second time on the same tile: last_tile (var 108, the tile's index 0xCF1) matches,
/// so do_qbert returns at once (WORLDEV.C:1912-1913) and the top stays 2. The port kept last_tile
/// in a byte, which could not hold 0xCF1, so the second step turned the top to the next colour.
///
/// Before all that, a qbert trap (owner 40, a colour room orb that matches nothing) is fired with
/// seq[0] cleared, so the sequence is reset: seq[0] = 6 and seq[1..6] = -1, which the original
/// stores as 0xFFFF in the 16-bit vars (WORLDEV.C:1892, 1897-1901). The port wrote 0xFF to the low
/// byte only, so a port save taken mid-puzzle held 0x00FF and DOS's -1 scan (WORLDEV.C:1919) ran on.
/// </summary>
public sealed class QbertGate : Scenario
{
    const int Level = 0x45;
    const int TopX = 0x31, TopY = 0x33;
    const int Colour = 2;
    const int GateLink2 = 0x4F;            // gate_links[2], WORLDEV.C:1882
    const int Moongate = 0x15A;            // ITEM_MOONGATE, uw2/src/include/items.h:506
    const ulong TeleportMs = 20000;

    public override string Name => "qbert-gate-stays";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:1880-1968";

    string _problem;
    uwObject _trap;
    int[] _emptyAfterReset;
    int _seq0AfterReset;
    int _topFloor, _gateInvis, _gateLink, _liftZ, _teleQuality, _teleOwner, _done, _gateItem, _gate2Invis, _topAfterRepeat, _gateInvisAfterRepeat;

    public override async Task Setup(ScenarioContext c)
    {
        Teleportation.Teleport(character: 0, tileX: TopX, tileY: TopY, newLevel: Level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            stable = (playerdat.dungeon_level == Level && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        if (playerdat.dungeon_level != Level) { _problem = $"the teleport to level {Level} did not happen (on level {playerdat.dungeon_level})"; return; }
        if (playerdat.playerObject.tileX != TopX || playerdat.playerObject.tileY != TopY)
        { _problem = $"the player is at ({playerdat.playerObject.tileX}, {playerdat.playerObject.tileY}), not on the pyramid top"; return; }
        var objs = UWTileMap.current_tilemap.LevelObjects;
        _gateItem = objs[0x3CE]?.item_id ?? -1;
        if (_gateItem != Moongate) { _problem = $"object 0x3CE is item 0x{_gateItem:X}, not a moongate"; return; }

        // A new sequence: with seq[0] not 6, any qbert trap resets it, seq[0] = 6 and seq[1..6] = -1
        // (WORLDEV.C:1897-1901). Owner 40 is a colour room orb that matches no colour, so nothing else happens.
        int slot = ObjectCreator.PrepareNewObject(0x183);   // ITEM_HACK_TRAP
        if (slot == 0) { _problem = "no hack trap could be made"; return; }
        _trap = objs[slot];
        _trap.quality = 32;
        _trap.owner = 40;
        playerdat.SetAt16(Seq(0), 0);
        a_hack_trap_qbert.Activate(_trap, objs);
        _seq0AfterReset = playerdat.GetAt16(Seq(0));
        _emptyAfterReset = new int[6];
        for (int i = 1; i <= 6; i++) _emptyAfterReset[i - 1] = playerdat.GetAt16(Seq(i));
        _trap.owner = 0x3F;

        playerdat.SetAt16(Seq(0), 6);
        playerdat.SetAt16(Seq(1), Colour);
        for (int i = 2; i <= 6; i++) playerdat.SetAt16(Seq(i), 0xFFFF);   // -1, free
        playerdat.SetAt16(Seq(4), Colour);        // seq[4], for the 0x29A gate (WORLDEV.C:1952)
        playerdat.SetGameVariable(107, 1);
        playerdat.SetAt16(0xFA + 108 * 2, 0);     // last_tile, a 16 bit var (WORLDEV.C:1895)
        for (int j = 0; j < 5; j++)
            for (int i = 0; i > j - 5; i--)
                UWTileMap.current_tilemap.Tiles[TopX + i, TopY + j].floorTexture = (short)Colour;
        UWTileMap.current_tilemap.Tiles[TopX, TopY].floorTexture = 6;
        objs[0x3CE].invis = 1;
        objs[0x29A].invis = 1;
    }

    /// <summary>Where seq[i] (var 100 + i, 16 bits) is in the port's player record.</summary>
    static int Seq(int i) => 0xFA + (100 + i) * 2;

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var trap = _trap;
        a_hack_trap_qbert.Activate(trap, UWTileMap.current_tilemap.LevelObjects);
        var objs = UWTileMap.current_tilemap.LevelObjects;
        _topFloor = UWTileMap.current_tilemap.Tiles[TopX, TopY].floorTexture;
        _gateInvis = objs[0x3CE].invis;
        _gateLink = objs[0x3CE].link;
        _liftZ = objs[0x3CC].zpos;
        _teleQuality = objs[0x3CD].quality;
        _teleOwner = objs[0x3CD].owner;
        _done = playerdat.GetGameVariable(107);
        _gate2Invis = objs[0x29A].invis;
        // the same tile again: last_tile matches, nothing happens (WORLDEV.C:1912-1913)
        a_hack_trap_qbert.Activate(trap, UWTileMap.current_tilemap.LevelObjects);
        _topAfterRepeat = UWTileMap.current_tilemap.Tiles[TopX, TopY].floorTexture;
        _gateInvisAfterRepeat = objs[0x3CE].invis;
        c.Note($"top floor {_topFloor}; gate 0x3CE invis {_gateInvis} link 0x{_gateLink:X}; 0x3CC z {_liftZ}; 0x3CD quality {_teleQuality} owner {_teleOwner}; var 107 {_done}; gate 0x29A invis {_gate2Invis}; after a repeat step top floor {_topAfterRepeat}, gate 0x3CE invis {_gateInvisAfterRepeat}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_topFloor != Colour) return $"precondition: the step did not turn the top tile to colour {Colour} (floor {_topFloor}; WORLDEV.C:1926)";
        var k = new Checks();
        k.Equal("moongate 0x3CE invisible after the pyramid is finished", 0, _gateInvis, "SET_INVIS(obj, 0): uw2 WORLDEV.C:1960; hidden only when not complete, :1962-1968");
        k.Equal("moongate 0x3CE link", 0x200 | GateLink2, _gateLink, "LINK_SPECIAL | gate_links[colour]: uw2 WORLDEV.C:1959");
        k.Equal("object 0x3CC z", 0x60, _liftZ, "SET_Z(obj, 0x60): uw2 WORLDEV.C:1943");
        k.Equal("object 0x3CD quality", 4, _teleQuality, "uw2 WORLDEV.C:1950");
        k.Equal("object 0x3CD owner", Colour * 6 + 4, _teleOwner, "uw2 WORLDEV.C:1951");
        k.Equal("var 107 (not complete)", 0, _done, "*done = 0: uw2 WORLDEV.C:1941");
        k.Equal("seq[0] after a new sequence", 6, _seq0AfterReset, "seq[0] = 6: uw2 WORLDEV.C:1898");
        for (int i = 1; i <= 6; i++)
            k.Equal($"seq[{i}] (var {100 + i}, 16 bits) in the player record after a new sequence", 0xFFFF, _emptyAfterReset[i - 1],
                "seq[i] = -1, int16 *seq = vars + 100: uw2 WORLDEV.C:1892, 1899-1900");
        k.Equal("moongate 0x29A invisible when the colour is seq[4]", 0, _gate2Invis, "seq[4] == colour: uw2 WORLDEV.C:1952-1954");
        k.Equal("top floor after a second step on the same tile", Colour, _topAfterRepeat, "last_tile matches, return: uw2 WORLDEV.C:1912-1913");
        k.Equal("moongate 0x3CE invisible after a second step on the same tile", 0, _gateInvisAfterRepeat, "uw2 WORLDEV.C:1912-1913");
        return k.Result;
    }
}
