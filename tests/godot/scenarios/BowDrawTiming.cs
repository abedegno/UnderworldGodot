using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// When a drawn bow can be let go (UW1 and UW2). The original's player_attack starts the weapon picture on its swing and
/// shows the aiming cursor and accepts a release only once the picture reaches its hold frame; let go before that, the
/// attack ends with nothing fired (uw1/src/combat/COMBAT.C:673-697; uw2/src/combat/COMBAT.C:729-753). The picture steps
/// each time the 256 Hz clock passes a multiple of 32 (update_screen, uw1/src/ui/PANELS.C:356; uw2/src/ui/PANELS.C:287).
/// UW1's hold is weap_frame 3, four steps on from -1 (uw1/src/ui/PANELS.C:856-866). UW2's hold is the step its frame
/// table marks 0x40, and a launcher has the idle table, where that is the third step (load_weapon(-1),
/// uw2/src/game/PLAYDATA.C:382-383; uw2/src/ui/PANELS.C:550, 671-685). So a bow fires no sooner than 3 * 32 ticks after
/// the press in UW1 and 2 * 32 in UW2, and always by 4 * 32 and 3 * 32.
///
/// Observed in DOS by Underworld Revisited (PQMarine, MIT): Assets/UWScripts/Interaction.cs:246-255, about half a second.
///
/// The scenario moves the player to open floor, puts a bow in the weapon hand and arrows in the pack, draws the bow, and
/// holds the attack key for 10 frames (40 ticks, short of either game's hold), then for 36 frames (144 ticks, past both).
/// The pointer is put at the middle of the 3D view, so the bow aims straight ahead. The short press must fire nothing,
/// and the long one one arrow: gone from the pack and in flight on the map.
///
/// The port fired once the attack charge passed the weapon's minimum (src/interaction/combat/combat_input.cs:287-292, 304),
/// with the minimum and the charge speed read from the melee weapon records 8 to 15 for the launchers 0x18 to 0x1F; for the
/// bow that is 75 at 9 (UW1) or 8 (UW2) every 16 ticks, so the 144-tick press fired nothing.
/// </summary>
public sealed class BowDrawTiming : Scenario
{
    const int Bow = 0x19;   // ITEM_BOW: uw1/src/include/items.h:174, uw2 items.h:159
    const int ShortFrames = 10, LongFrames = 36;

    public override string Name => "bow-draw-timing";
    public override string Cites => "underworld-exhumed uw1/src/combat/COMBAT.C:673-697, uw1/src/ui/PANELS.C:356, 856-866; uw2/src/combat/COMBAT.C:729-753, uw2/src/ui/PANELS.C:287, 550, 671-685, uw2/src/game/PLAYDATA.C:382-383";

    string _problem;
    int _arrow, _before, _afterShort, _afterLong, _inFlight;
    uint _shortTicks, _longTicks;

    /// <summary>Arrows on the map as mobile objects, that is, in flight.</summary>
    int MobileArrows()
    {
        var map = UWTileMap.current_tilemap;
        int n = 0;
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.item_id == _arrow && !o.IsStatic) n++;
                    i = o.next;
                }
        return n;
    }

    int Arrows() => playerdat.InventoryObjects.Where(o => o != null && o.item_id == _arrow).Sum(o => o.ObjectQuantity);

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        // open, level ground all round, so the arrow has room whichever way the player faces
        var (fx, fy, _, _) = TradeGround.FindPair();
        if (fx < 0) { _problem = "no open floor with room to shoot"; return; }
        if (!await TradeGround.MovePlayer(c, fx, fy)) { _problem = $"the player did not reach ({fx}, {fy})"; return; }
        foreach (int slot in new[] { 7, 8 }) BatchB.Unequip(slot);
        foreach (int slot in new[] { 7, 8 })
        {
            var o = BatchB.Equip(Bow, slot, 40);
            if (o == null) continue;
            if (playerdat.PrimaryHandObject == o) break;
            BatchB.Unequip(slot);
        }
        if (playerdat.PrimaryHandObject?.item_id != Bow) { _problem = "the bow did not reach the weapon hand"; return; }
        _arrow = 16 + rangedObjectDat.RangedWeaponType(Bow);
        var (index, _) = Guards.PutInBackpack(_arrow, o => { o.is_quant = 1; o.link = 5; });
        if (index == 0) { _problem = "no arrows could be put in the pack"; return; }
        _before = Arrows();
    }

    async Task<uint> Press(ScenarioContext c, int frames)
    {
        uint start = main.GlobalPITTimer;
        Guards.Key(Key.P, true);
        await c.Frames(frames);
        uint held = main.GlobalPITTimer - start;
        Guards.Key(Key.P, false);
        _inFlight = 0;
        for (int f = 0; f < 20; f++)
        {
            await c.Frames(1);
            _inFlight = System.Math.Max(_inFlight, MobileArrows());
        }
        return held;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        uimanager.ToggleWeaponAnimationState(true, false);
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) await c.Frames(1);
        if (combat.stage != combat.CombatStages.Ready) { _problem = $"the bow never became ready ({combat.stage})"; return; }
        // Aim straight ahead: the pointer at the middle of the 3D view, as player_settr reads it (missile_trx 1, missile_try
        // from the pitch alone: uw1 MISSILE.C:64-81, uw2 MISSILE.C:55-73). The port aims from the last click
        // (motion.InitPlayerProjectileValues), which headless is the top left corner, the steepest aim up; from a square of
        // floor height 12 that puts the arrow's height past 127, where the 7-bit z wraps (SET_Z, uw2 include/object.h:205)
        // and the arrow lands under the floor, so it had no room.
        uimanager.ViewPortMouseXPos = 4 * (uimanager.Window3DLeftBorder + uimanager.Window3DHeadingAdjust);
        uimanager.ViewPortMouseYPos = 4 * (200 - 54 - uimanager.Window3DPitchAdjust);
        _shortTicks = await Press(c, ShortFrames);
        _afterShort = Arrows();
        for (int f = 0; f < 120 && combat.stage != combat.CombatStages.Ready; f++) await c.Frames(1);
        _longTicks = await Press(c, LongFrames);
        _afterLong = Arrows();
        c.Note($"arrows {_before}; after a {_shortTicks}-tick press {_afterShort}; after a {_longTicks}-tick press {_afterLong}; projectiles in flight {_inFlight}");
        uimanager.ToggleWeaponAnimationState(false, false);
        await c.Frames(30);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw1 = c.Game == "uw1";
        uint earliest = uw1 ? 3u * 32 : 2u * 32, latest = uw1 ? 4u * 32 : 3u * 32;
        if (_shortTicks >= earliest) return $"precondition: the short press lasted {_shortTicks} ticks, not under {earliest}";
        if (_longTicks <= latest) return $"precondition: the long press lasted {_longTicks} ticks, not over {latest}";
        var k = new Checks();
        k.Equal($"arrows fired by a {_shortTicks}-tick press", 0, _before - _afterShort, uw1 ? "let go before the hold frame: uw1 COMBAT.C:673-679" : "let go before the hold frame: uw2 COMBAT.C:729-735");
        k.Equal($"arrows in flight after the {_longTicks}-tick press", 1, _inFlight, uw1 ? "player_fire, missile_fire: uw1 MISSILE.C:96-106" : "player_fire, missile_fire: uw2 MISSILE.C:93-102");
        k.Equal($"arrows fired by a {_longTicks}-tick press", 1, _afterShort - _afterLong, uw1 ? "let go at the hold frame: uw1 COMBAT.C:690-696" : "let go at the hold frame: uw2 COMBAT.C:746-752");
        return k.Result;
    }
}
