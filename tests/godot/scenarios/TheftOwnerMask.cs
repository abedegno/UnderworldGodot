using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Whose an owned object is after the player picks it up (UW1 and UW2). The original's
/// player_grabbed tells the owners nearby and then makes the object nobody's when its owner,
/// without bit 5, is at most 0x1B in UW1 (uw1/src/critter/CRITTIME.C:480-481) and 0x1D in UW2
/// (uw2/src/critter/CRITTIME.C:545-546). UW2 also runs clear_owner over a container and its
/// contents (Obj_Check, uw2/src/critter/CRITTIME.C:547-548), whatever the container's own owner;
/// UW1 leaves the contents alone.
///
/// The port's thief.FlagTheftToObjectOwner (src/interaction/thief.cs) cleared the owner when it
/// was at most 29 in both games, with no mask, and cleared a container's contents in both games,
/// only when the container's own owner was cleared.
///
/// The scenario puts owned objects one at a time on the player's tile and picks each up through
/// pickup.PickUp: coins owned by 0x21, 0x1C and 0x1E, a sack owned by 0x01 holding a coin owned
/// by 0x05, and a sack owned by 0x3E holding a coin owned by 0x05.
/// </summary>
public sealed class TheftOwnerMask : Scenario
{
    const int Sack = 0x80;   // ITEM_SACK, uwN/src/include/items.h

    public override string Name => "theft-owner-mask";
    public override string Cites => "underworld-exhumed uw1/src/critter/CRITTIME.C:470-483 (player_grabbed); uw2/src/critter/CRITTIME.C:524-549 (clear_owner, player_grabbed), uw2/src/obj/OBJECTS.C:104-121 (Obj_Check)";

    string _problem;
    int _px, _py;
    readonly List<(string what, int owner, int got)> _coins = new();
    readonly List<(int sackOwner, int sackGot, int contentGot)> _sacks = new();

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;

    public override Task Setup(ScenarioContext c)
    {
        _px = playerdat.playerObject.npc_xhome;
        _py = playerdat.playerObject.npc_yhome;
        if (!commonObjDat.canhaveowner(ConvHelp.Coin)) _problem = "coins cannot be owned";
        else if (!commonObjDat.canhaveowner(Sack)) _problem = "a sack cannot be owned";
        return Task.CompletedTask;
    }

    /// <summary>An object at the head of the player's tile's list.</summary>
    int MakeOnTile(int item, int owner)
    {
        int i = ObjectCreator.PrepareNewObject(item);
        if (i == 0) return 0;
        var o = Objs[i];
        var tile = UWTileMap.current_tilemap.Tiles[_px, _py];
        o.owner = (short)owner;
        o.zpos = (short)(tile.floorHeight << 3);
        o.tileX = _px; o.tileY = _py;
        o.next = tile.indexObjectList;
        tile.indexObjectList = (short)i;
        ObjectCreator.RenderObject(o, UWTileMap.current_tilemap);
        return i;
    }

    static void EmptyHand()
    {
        if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        try
        {
            useon.CurrentItemBeingUsed = null;
            foreach (int owner in new[] { 0x21, 0x1C, 0x1E })
            {
                EmptyHand();
                int coin = MakeOnTile(ConvHelp.Coin, owner);
                if (coin == 0) { _problem = "no coin could be made"; return Task.CompletedTask; }
                bool picked = pickup.PickUp(coin, Objs, WorldObject: true);
                if (!picked || playerdat.ObjectInHand != coin) { _problem = $"the coin owned by 0x{owner:X} was not picked up"; return Task.CompletedTask; }
                _coins.Add(($"coin owned by 0x{owner:X}", owner, Objs[coin].owner));
                c.Note($"coin owned by 0x{owner:X}: owner after the pick-up 0x{Objs[coin].owner:X}");
            }
            foreach (int owner in new[] { 0x01, 0x3E })
            {
                EmptyHand();
                int sack = MakeOnTile(Sack, owner);
                int content = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
                if (sack == 0 || content == 0) { _problem = "no sack or coin could be made"; return Task.CompletedTask; }
                Objs[content].owner = 5;
                Objs[content].next = 0;
                Objs[sack].is_quant = 0;
                Objs[sack].link = (short)content;
                bool picked = pickup.PickUp(sack, Objs, WorldObject: true);
                if (!picked || playerdat.ObjectInHand != sack) { _problem = $"the sack owned by 0x{owner:X} was not picked up"; return Task.CompletedTask; }
                _sacks.Add((owner, Objs[sack].owner, Objs[content].owner));
                c.Note($"sack owned by 0x{owner:X} holding a coin owned by 0x5: owners after the pick-up 0x{Objs[sack].owner:X} and 0x{Objs[content].owner:X}");
            }
        }
        finally
        {
            EmptyHand();
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        int limit = uw2 ? 0x1D : 0x1B;
        string grab = uw2 ? "uw2 CRITTIME.C:545-546" : "uw1 CRITTIME.C:480-481";
        var k = new Checks();
        foreach (var (what, owner, got) in _coins)
            k.Equal($"owner of the {what} after the pick-up", (owner & 0x1F) <= limit ? 0 : owner, got, grab);
        foreach (var (sackOwner, sackGot, contentGot) in _sacks)
        {
            // UW2's clear_owner clears the sack and its contents whatever the sack's own owner (uw2 CRITTIME.C:547-548)
            int sackWant = uw2 || (sackOwner & 0x1F) <= limit ? 0 : sackOwner;
            k.Equal($"owner of the sack owned by 0x{sackOwner:X} after the pick-up", sackWant, sackGot, uw2 ? "uw2 CRITTIME.C:545-548" : grab);
            k.Equal($"owner of the coin in the sack owned by 0x{sackOwner:X}", uw2 ? 0 : 5, contentGot, uw2 ? "uw2 CRITTIME.C:547-548" : "uw1 CRITTIME.C:470-483, no contents");
        }
        return k.Result;
    }
}
