using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>Helpers for the death, remains and arena scenarios.</summary>
static class Remains
{
    static UWTileMap Map => UWTileMap.current_tilemap;

    /// <summary>The square within r of (cx, cy) whose object list holds index, or (-1, -1).</summary>
    public static (int x, int y) FindInTiles(int index, int cx, int cy, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (!UWTileMap.ValidTile(x, y)) continue;
                int i = Map.Tiles[x, y].indexObjectList, guard = 0;
                while (i != 0 && guard++ < 1024)
                {
                    if (i == index) return (x, y);
                    i = Map.LevelObjects[i].next;
                }
            }
        return (-1, -1);
    }

    /// <summary>The objects within r of (cx, cy) whose item id pick accepts and whose index is not in before.</summary>
    public static List<uwObject> NewObjects(int cx, int cy, int r, Func<uwObject, bool> pick, HashSet<int> before)
    {
        var found = new List<uwObject>();
        for (int x = cx - r; x <= cx + r; x++)
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (!UWTileMap.ValidTile(x, y)) continue;
                int i = Map.Tiles[x, y].indexObjectList, guard = 0;
                while (i != 0 && guard++ < 1024)
                {
                    var o = Map.LevelObjects[i];
                    if (!before.Contains(i) && pick(o)) found.Add(o);
                    i = o.next;
                }
            }
        return found;
    }

    /// <summary>Every object index in the tile lists within r of (cx, cy).</summary>
    public static HashSet<int> Snapshot(int cx, int cy, int r)
    {
        var set = new HashSet<int>();
        foreach (var o in NewObjects(cx, cy, r, _ => true, new HashSet<int>())) set.Add(o.index);
        return set;
    }

    /// <summary>Whether an object's position (eighths of a tile) is within range of (x, y) on both axes.</summary>
    public static bool Near(uwObject o, int x, int y, int range) =>
        Math.Abs((o.tileX << 3) + o.xpos - x) <= range && Math.Abs((o.tileY << 3) + o.ypos - y) <= range;

    /// <summary>Moves the player to a level with no square, as the death scenarios do, and back to play.</summary>
    public static void GoToLevel(int level)
    {
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, level, 0);
        Teleportation.HandleTeleportation();
    }

    /// <summary>Answers and closes any conversation that started, as GuardTalk does.</summary>
    public static async Task EndConversation(ScenarioContext c)
    {
        ulong deadline = Time.GetTicksMsec() + 20000;
        while (Time.GetTicksMsec() < deadline && uimanager.CurrentGameMode == uimanager.GameModes.CONVERSATION)
        {
            if (MessageDisplay.WaitingForMore) MessageDisplay.WaitingForMore = false;
            if (MessageDisplay.WaitingForTypedInput) MessageDisplay.WaitingForTypedInput = false;
            if (ConversationVM.WaitingForInput)
            {
                ConversationVM.PlayerNumericAnswer = Math.Max(1, ConversationVM.MaxAnswer);
                ConversationVM.WaitingForInput = false;
            }
            await c.Frames(1);
        }
    }
}

/// <summary>
/// An object held on the cursor is dropped on death and on waking from the Ethereal Void. UW2's player_is_dead calls
/// punt_mouse_obj before the move to the blackrock gem (uw2/src/game/SKILLS.C:848), and punt_void calls it before the
/// teleport back from the dream (SKILLS.C:751); punt_mouse_obj puts the object near the player with
/// near_mob_put_at(ThePlayer, CursorObjPtr, 6, 0) and empties the cursor (SKILLS.C:731-743). UW1's player_is_dead has
/// the same code inline, and then leaves a skull or bones, 0xC2 + rand() % 5, with put_at at the player's position
/// (range 0, no culling), his height and fine position, and owner 0x3F (uw1/src/game/SKILLS.C:836-860).
///
/// The port's death paths (src/player/playerdatdeath.cs) and AwakenFromTheVoid (src/World/sleep.cs) left the held
/// object on the cursor; only the later level change dropped it, at the player's square with fine position 3, 3 and no
/// fit test, and UW1 left no bones.
///
/// UW1: a coin is held, no silver tree is planted, and PlayerDeathUW1 runs; the object and the bones are checked at
/// once, then the death cutscene is stopped. UW2: A. the player is moved to level 9, holds a coin and PlayerDeathUW2
/// runs; the object is checked before the queued move to level 5 is carried out. B. the dream is set to return to the
/// player's own square and AwakenFromTheVoid runs with a coin held.
/// </summary>
public sealed class DeathDropsCursorObject : Scenario
{
    const int Coin = 0xA0;

    public override string Name => "death-drops-cursor-object";
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:731-751, 848; uw1/src/game/SKILLS.C:836-860; uw2/src/motion/COLLIDE.C:631-658";

    string _problem;
    readonly List<string> _results = new();
    int _bonesFound, _bonesOwner = -1;
    bool _bonesAtPlayer;
    bool _menu;

    static readonly MethodInfo DeathUW1 = typeof(playerdat).GetMethod("PlayerDeathUW1", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo DeathUW2 = typeof(playerdat).GetMethod("PlayerDeathUW2", BindingFlags.NonPublic | BindingFlags.Static);

    public override Task Setup(ScenarioContext c)
    {
        if (DeathUW1 == null || DeathUW2 == null) _problem = "playerdat.PlayerDeathUW1 or PlayerDeathUW2 not found";
        return Task.CompletedTask;
    }

    /// <summary>Holds a new coin; returns its index.</summary>
    static int Hold()
    {
        playerdat.ObjectInHand = -1;
        return ObjectCreator.SpawnObjectInHand(Coin);
    }

    /// <summary>Records where the held object went: off the cursor, in a square, within 6 eighths of the player.</summary>
    void Check(string part, int held, int px, int py)
    {
        var (tx, ty) = Remains.FindInTiles(held, playerdat.playerObject.tileX, playerdat.playerObject.tileY, 2);
        var o = UWTileMap.current_tilemap.LevelObjects[held];
        bool near = tx >= 0 && Remains.Near(o, px, py, 6);
        if (playerdat.ObjectInHand != -1) _results.Add($"{part}: the object is still held on the cursor (near_mob_put_at then CursorObjPtr = 0: uw2 SKILLS.C:735-739)");
        else if (tx < 0) _results.Add($"{part}: the dropped object is in no square near the player");
        else if (!near) _results.Add($"{part}: the dropped object at fine ({(o.tileX << 3) + o.xpos}, {(o.tileY << 3) + o.ypos}) is more than 6 eighths from the player at ({px}, {py}) (near_mob_put_at range 6: uw2 SKILLS.C:736)");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var p = playerdat.playerObject;
        if (c.Game == "uw1")
        {
            // die on an empty square, so nothing there that the bones could land on
            var sq = DeathCutscene.FindEmptySquare();
            if (sq.x < 0) { _problem = "no empty open square"; return; }
            Teleportation.Teleport(0, sq.x, sq.y, 0, 0);
            Teleportation.HandleTeleportation();
            await c.Frames(2);
            int held = Hold();
            if (held <= 0) { _problem = "no coin could be held"; return; }
            int px = (p.tileX << 3) + p.xpos, py = (p.tileY << 3) + p.ypos;
            var before = Remains.Snapshot(p.tileX, p.tileY, 1);
            playerdat.SilverTreeDungeon = 0;
            // the player is taken out of his square's list, or obj_deal finds the bones inside him and, unless they are
            // tenacious, culls them (uw1 OBJPHYS.C:514-593, OBJECTS.C:154-170); that is the original's rule too, but it
            // would hide whether the bones were made and placed
            ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(p.tileX, p.tileY, (short)p.index, RemoveFromWorld: false);
            DeathCutscene.Forget();
            DeathUW1.Invoke(null, null);
            Check("UW1 death", held, px, py);
            var bones = Remains.NewObjects(p.tileX, p.tileY, 1, o => o.item_id >= 0xC2 && o.item_id <= 0xC6, before);
            _bonesFound = bones.Count;
            if (bones.Count > 0)
            {
                _bonesOwner = bones[0].owner;
                _bonesAtPlayer = bones[0].tileX == p.tileX && bones[0].tileY == p.tileY && bones[0].xpos == p.xpos && bones[0].ypos == p.ypos;
            }
            c.Note($"UW1: held {held}; bones made {_bonesFound}, owner {_bonesOwner}, at the player's spot {_bonesAtPlayer}");
            _menu = await DeathCutscene.StopAndReachMenu(c);
            return;
        }

        // A: death outside Britannia.
        Remains.GoToLevel(9);
        if (playerdat.dungeon_level != 9) { _problem = $"the move to level 9 left the player on level {playerdat.dungeon_level}"; return; }
        p = playerdat.playerObject;//the new level's
        int heldA = Hold();
        if (heldA <= 0) { _problem = "no coin could be held"; return; }
        int ax = (p.tileX << 3) + p.xpos, ay = (p.tileY << 3) + p.ypos;
        DeathCutscene.Forget();
        DeathUW2.Invoke(null, null);
        Check("A: death on level 9", heldA, ax, ay);
        Teleportation.HandleTeleportation();
        playerdat.play_hp = playerdat.max_hp;
        await c.Frames(2);
        p = playerdat.playerObject;

        // B: waking from the Void, back to this square.
        int heldB = Hold();
        if (heldB <= 0) { _problem = "no coin could be held (B)"; return; }
        int bx = (p.tileX << 3) + p.xpos, by = (p.tileY << 3) + p.ypos;
        playerdat.DreamingInVoid = true;
        playerdat.DreamDungeon = playerdat.dungeon_level;
        playerdat.DreamTileX = p.tileX;
        playerdat.DreamTileY = p.tileY;
        sleep.AwakenFromTheVoid();
        Check("B: waking from the Void", heldB, bx, by);
        Teleportation.HandleTeleportation();
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var r in _results) k.True(false, r);
        if (c.Game == "uw1")
        {
            k.Equal("UW1: bones left where the player died", 1, _bonesFound, "CreateObj(rand() % 5 + ITEM_SKULL_C2), put_at: uw1 SKILLS.C:852-853");
            if (_bonesFound > 0)
            {
                k.Equal("UW1: owner of the player's bones", 0x3F, _bonesOwner, "bones->ol.f.owner = 0x3F: uw1 SKILLS.C:856");
                k.True(_bonesAtPlayer, "UW1: the bones expected on the player's square at his fine position (uw1 SKILLS.C:857-859)");
            }
            k.True(_menu, "UW1: the game expected back at the main menu after the death cutscene");
        }
        return k.Result;
    }
}

/// <summary>
/// A dead critter's remains and loot (both games). The critter loop takes a dying critter off its square, makes its
/// loot, runs build_corpse and drop_some_objects, and frees it (uw1/src/critter/AI.C:1140-1144, uw2 AI.C:1112-1116).
/// build_corpse leaves the fluids (0xD8 + n in UW1, 0xD9 + n in UW2) at the critter's fine x, fine y and height with
/// quality 0x28, and 7 times in 16 (always in UW2's Pits of Carnage, world 7) a corpse 0xC0 + n owned by the creature
/// type, placed with near_mob_put_at(critter, remains, 4, 0) (uw1 PATHFIND.C:146-167, uw2 PATHFIND.C:146-167).
/// drop_link_chain puts each carried object round the critter with put_at(x, y, z, obj, 6, 0); UW2 gives each one that
/// can be owned to the critter's race and puts out lit lights, UW1 leaves the objects' owners alone
/// (uw1 TREASURE.C:49-80, uw2 TREASURE.C:37-75).
///
/// The port (src/npc/npcdeath.cs DropRemainsAndLoot) spilled the loot at random spots of the square with owner 0, put
/// the fluids and the corpse at fine y = the critter's height, made no corpse at all in the Pits, and gave the corpse no
/// owner.
///
/// Five critters with fluids and a corpse are killed with npc.KillCritter, each from its own seed, each carrying an
/// object of owner 5 (UW2 also a lit lantern). UW2 then kills three in the Pits (level 57).
/// </summary>
public sealed class CritterRemainsPutAt : Scenario
{
    public override string Name => "critter-remains-put-at";
    public override string Cites => "underworld-exhumed uw1/src/critter/PATHFIND.C:146-167, uw2/src/critter/PATHFIND.C:146-167; uw1/src/obj/TREASURE.C:49-80, uw2/src/obj/TREASURE.C:37-75";

    string _problem;
    readonly List<string> _fails = new();
    int _corpses, _pitsKills, _pitsCorpses, _id = -1;

    public override Task Setup(ScenarioContext c)
    {
        _id = BatchB.FindCritter(id => critterObjectDat.fluids(id) != 0 && critterObjectDat.corpse(id) != 0);
        if (_id < 0) _problem = "no creature with both fluids and a corpse";
        return Task.CompletedTask;
    }

    /// <summary>The first item id from 0 that can be owned and is not a container or a light.</summary>
    static int OwnableItem()
    {
        for (int id = 0; id < 0x80; id++)
            if (commonObjDat.canhaveowner(id) && commonObjDat.stackable(id) != 1 && commonObjDat.CanBePickedUp(id)) return id;
        return -1;
    }

    /// <summary>Kills one critter at (x, y) from seed; returns whether a corpse was made.</summary>
    bool KillOne(ScenarioContext c, string part, int x, int y, uint seed)
    {
        var map = UWTileMap.current_tilemap;
        var critter = BatchB.SpawnCritter(_id, x, y);
        if (critter == null) { _fails.Add($"{part}: no critter could be made"); return false; }
        critter.npc_whoami = 0;
        critter.xpos = 2; critter.ypos = 5;
        critter.LootSpawnedFlag = 1;
        int cx = (x << 3) + 2, cy = (y << 3) + 5, cz = critter.zpos;
        // what it carries: an ownable object of owner 5, and in UW2 a lit lantern
        var carried = new List<uwObject>();
        foreach (var itemId in c.Game == "uw2" ? new[] { OwnableItem(), 0x94 } : new[] { OwnableItem() })
        {
            int slot = ObjectCreator.PrepareNewObject(itemId);
            if (slot == 0) continue;
            var o = map.LevelObjects[slot];
            o.owner = 5;
            o.next = critter.link;
            critter.link = (short)slot;
            carried.Add(o);
        }
        critter.is_quant = 0;
        var before = Remains.Snapshot(x, y, 2);
        int fluidsId = critterObjectDat.fluids(_id) + (c.Game == "uw2" ? 0xD9 : 0xD8);
        int corpseId = critterObjectDat.corpse(_id) + 0xC0;
        Rng.Source = new BorlandRandSource(seed);
        npc.KillCritter(critter);

        if (Remains.FindInTiles(critter.index, x, y, 0).x >= 0) _fails.Add($"{part}: the critter is still on its square");
        var stains = Remains.NewObjects(x, y, 0, o => o.item_id == fluidsId, before);
        if (stains.Count != 1) _fails.Add($"{part}: {stains.Count} fluids 0x{fluidsId:X} on the critter's square, expected 1 (uw2 PATHFIND.C:151-160)");
        else
        {
            var s = stains[0];
            if (s.xpos != 2 || s.ypos != 5) _fails.Add($"{part}: fluids at fine ({s.xpos}, {s.ypos}), expected the critter's (2, 5) (SET_FINEX/SET_FINEY: uw2 PATHFIND.C:153-154)");
            if (s.zpos != cz) _fails.Add($"{part}: fluids at height {s.zpos}, expected the critter's {cz} (SET_Z: uw2 PATHFIND.C:155)");
            if (s.quality != 0x28) _fails.Add($"{part}: fluids quality {s.quality}, expected 0x28 (uw2 PATHFIND.C:156)");
        }
        var corpses = Remains.NewObjects(x, y, 2, o => o.item_id == corpseId, before);
        foreach (var o in corpses)
        {
            if (o.owner != (_id & 0x3F)) _fails.Add($"{part}: corpse owner {o.owner}, expected the creature type {_id & 0x3F} (owner = OBJ_INMAJOR(obj): uw2 PATHFIND.C:163)");
            if (!Remains.Near(o, cx, cy, 4)) _fails.Add($"{part}: corpse at ({(o.tileX << 3) + o.xpos}, {(o.tileY << 3) + o.ypos}) more than 4 eighths from the critter at ({cx}, {cy}) (near_mob_put_at(obj, remains, 4, 0): uw2 PATHFIND.C:164)");
        }
        foreach (var o in carried)
        {
            var (tx, _) = Remains.FindInTiles(o.index, x, y, 2);
            if (tx < 0) { _fails.Add($"{part}: carried object {o.index} not dropped near the critter"); continue; }
            if (!Remains.Near(o, cx, cy, 6)) _fails.Add($"{part}: carried object at ({(o.tileX << 3) + o.xpos}, {(o.tileY << 3) + o.ypos}) more than 6 eighths from ({cx}, {cy}) (put_at(x, y, z, obj, 6, 0): TREASURE.C)");
            int expectedOwner = c.Game == "uw2" && commonObjDat.canhaveowner(o.item_id) ? critterObjectDat.faction(_id) : 5;
            if (o.owner != expectedOwner) _fails.Add($"{part}: carried 0x{o.item_id:X} owner {o.owner}, expected {expectedOwner} ({(c.Game == "uw2" ? "the race when it can be owned: uw2 TREASURE.C:60-61" : "unchanged, UW1 sets the container's: uw1 TREASURE.C:72-73")})");
        }
        if (c.Game == "uw2" && carried.Count > 1 && carried[1].item_id != 0x90) _fails.Add($"{part}: the lit lantern is 0x{carried[1].item_id:X}, expected put out to 0x90 (uw2 TREASURE.C:62-63)");
        c.Note($"{part}: seed {seed}, critter 0x{_id:X}, {corpses.Count} corpse(s)");
        return corpses.Count > 0;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        var sq = DeathCutscene.FindEmptySquare();
        if (sq.x < 0) { _problem = "no empty open square"; return; }
        for (uint s = 1; s <= 5; s++)
        {
            if (KillOne(c, $"kill {s}", sq.x, sq.y, s)) _corpses++;
            await c.Frames(1);
        }
        if (c.Game != "uw2") return;
        Remains.GoToLevel(57);
        if (playerdat.dungeon_level != 57) { _problem = $"the move to level 57 left the player on level {playerdat.dungeon_level}"; return; }
        var pit = DeathCutscene.FindEmptySquare();
        if (pit.x < 0) { _problem = "no empty open square on level 57"; return; }
        for (uint s = 1; s <= 3; s++)
        {
            _pitsKills++;
            if (KillOne(c, $"Pits kill {s}", pit.x, pit.y, s)) _pitsCorpses++;
            await c.Frames(1);
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        foreach (var f in _fails) k.True(false, f);
        k.True(_corpses > 0, "no corpse made in five kills, so its placement and owner were not tested (7 in 16: PATHFIND.C:161)");
        if (c.Game == "uw2") k.Equal("corpses in the Pits of Carnage", _pitsKills, _pitsCorpses, "always in world 7: uw2 PATHFIND.C:161");
        return k.Result;
    }
}

/// <summary>
/// A rock hammer used on a boulder (both games). UseRockHammerOn breaks a boulder 0x153..0x156 into
/// rand() * 2 / 0x8000 + (0x155 - id) + 1 pieces (its first draw), each id + rand() * 2 / 0x8000 + 1 and anything past
/// the small boulder 0x156 a stack of 3 to 8 sling stones (0x10), and puts each one round where the boulder lay with
/// put_at(x, y, z, rock, 6, 0); the boulder is removed (uw1/src/obj/USEITEMS.C:724-763, uw2 USEITEMS.C:1002-1049).
/// UW1 copies the boulder's object record into each piece before setting its item (USEITEMS.C:747) and removes the
/// boulder only after the pieces are placed, so a piece that finds no room within 24 tries can be culled by put_at;
/// UW2 removes the boulder first (USEITEMS.C:1028).
///
/// The port's rockhammer.UseOn (src/objects/rockhammer.cs) made one new piece, always one size smaller
/// (Rng.r.Next(1, 2) is always 1), at the boulder's own spot.
///
/// A large boulder (0x153) of quality 17 and owner 9 is put on an empty square and broken from seed 1. Every piece is
/// one or two sizes smaller and within 6 eighths of the boulder's spot, and the boulder is gone. UW2 has exactly the
/// count the first draw gives; UW1 has at most that many, each with the boulder's quality and owner.
/// </summary>
public sealed class BoulderBreaks : Scenario
{
    const int Large = 0x153;
    const uint BreakSeed = 1;

    public override string Name => "boulder-breaks";
    public override string Cites => "underworld-exhumed uw1/src/obj/USEITEMS.C:724-763, uw2/src/obj/USEITEMS.C:1002-1049";

    string _problem;
    int _pieces, _boulderLeft;
    readonly List<string> _fails = new();

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        var sq = DeathCutscene.FindEmptySquare();
        if (sq.x < 0) { _problem = "no empty open square"; return Task.CompletedTask; }
        var boulder = BatchB.SpawnStatic(Large, sq.x, sq.y);
        if (boulder == null) { _problem = "no boulder could be made"; return Task.CompletedTask; }
        boulder.xpos = 3; boulder.ypos = 3;
        boulder.quality = 17; boulder.owner = 9;
        int bx = (sq.x << 3) + 3, by = (sq.y << 3) + 3;
        var before = Remains.Snapshot(sq.x, sq.y, 2);
        before.Remove(boulder.index);
        Rng.Source = new BorlandRandSource(BreakSeed);
        rockhammer.UseOn(null, boulder, true);
        _boulderLeft = Remains.FindInTiles(boulder.index, sq.x, sq.y, 0).x >= 0 && boulder.item_id == Large ? 1 : 0;
        var pieces = Remains.NewObjects(sq.x, sq.y, 2, o => o.item_id == 0x154 || o.item_id == 0x155, before);
        _pieces = pieces.Count;
        foreach (var o in pieces)
        {
            if (!Remains.Near(o, bx, by, 6)) _fails.Add($"piece 0x{o.item_id:X} at ({(o.tileX << 3) + o.xpos}, {(o.tileY << 3) + o.ypos}) more than 6 eighths from the boulder at ({bx}, {by}) (put_at(.., rock, 6, 0))");
            if (c.Game == "uw1" && (o.quality != 17 || o.owner != 9)) _fails.Add($"UW1 piece 0x{o.item_id:X} quality {o.quality} owner {o.owner}, expected the boulder's 17 and 9 (*(struct StaticObj far *)rock = *obj: uw1 USEITEMS.C:747)");
        }
        c.Note($"boulder at {sq}; pieces: {string.Join(", ", pieces.ConvertAll(o => $"0x{o.item_id:X} at {o.tileX},{o.tileY} fine {o.xpos},{o.ypos}"))}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        int count = (int)((long)new BorlandRandSource(BreakSeed).Rand() * 2 / 0x8000) + (0x155 - Large) + 1;
        if (c.Game == "uw2") k.Equal("pieces from a large boulder", count, _pieces, "rand() * 2L / 0x8000L + (ITEM_BOULDER - id) + 1: uw2 USEITEMS.C:1029");
        else k.Within("pieces from a large boulder (some may find no room and be culled)", 1, count, _pieces, "rand() * 2L / 0x8000L + (ITEM_BOULDER - id) + 1: uw1 USEITEMS.C:743");
        k.Equal("large boulders left on the square", 0, _boulderLeft, "Obj_Punt: uw1 USEITEMS.C:759, uw2 USEITEMS.C:1028");
        foreach (var f in _fails) k.True(false, f);
        return k.Result;
    }
}

/// <summary>
/// The Tombs once Loth is laid to rest (UW2). Arriving in any level of the Tombs (world 6) with quest 7 set runs
/// genocide(0xFF) (uw2/src/game/GAMEWRAP.C:495-497), which kills the undead critters and passes every floating skull
/// (item 0x13) among the active mobiles to destroy_floatskull, which makes it a plain skull, 0xC3
/// (uw2/src/event/WORLDEV.C:1649-1660, 1686-1698).
///
/// The port's tomb.KillLothsLiches (src/World/specificworlds/tomb.cs) only printed a line for the floating skull, so
/// the one on level 50 went on floating.
///
/// Quest 7 is set and the player is moved to level 50: no floating skull is left among the mobiles, and a plain skull
/// holds a mobile slot (the destroyed one, the precondition that level 50 had one).
/// </summary>
public sealed class TombFloatSkulls : Scenario
{
    public override string Name => "tomb-floatskulls-destroyed";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/GAMEWRAP.C:495-497; uw2/src/event/WORLDEV.C:1649-1660, 1686-1698";

    string _problem;
    int _floating, _plainMobile;

    public override Task Setup(ScenarioContext c)
    {
        playerdat.SetQuest(7, 1);
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        Remains.GoToLevel(50);
        if (playerdat.dungeon_level != 50) { _problem = $"the move to level 50 left the player on level {playerdat.dungeon_level}"; return; }
        await c.Frames(2);
        var map = UWTileMap.current_tilemap;
        for (int i = 0; i < map.NoOfActiveMobiles; i++)
        {
            var o = map.LevelObjects[map.GetActiveMobileAtIndex(i)];
            if (o.item_id == 0x13) _floating++;
            if (o.item_id == 0xC3) _plainMobile++;
        }
        c.Note($"level 50 active mobiles: {_floating} floating skull(s), {_plainMobile} plain skull(s)");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_floating == 0 && _plainMobile == 0) return "precondition: level 50 has no floating skull among its active mobiles";
        var k = new Checks();
        k.Equal("floating skulls left on level 50 once Loth is laid to rest", 0, _floating, "genocide(0xFF) -> destroy_floatskull: uw2 GAMEWRAP.C:495-497, WORLDEV.C:1694");
        return k.Result;
    }
}

/// <summary>
/// Running from a pit fight by teleporting (UW2). do_teleport, for a player in the pits, calls arena_player_runs when
/// the destination is another level, or a square outside the arena ring (in_arena), or in another quarter of it than
/// the player's (mpos(x - 0x1F, y - 0x1F) >> 1) (uw2/src/event/WORLDEV.C:212-218, 963-976). arena_player_runs stops
/// the fighters (attitude 1, goal 1), the first talks to the player, and the fight, the pit record (quest 129) and
/// Jospur's debt (quest 133) are cleared (WORLDEV.C:2029-2055). A level of 0 with a real square is the same level
/// (WORLDEV.C:198-199).
///
/// The port (src/utility/teleportation.cs TeleportUW2) only counted a level change, which included a level of 0, and
/// left a TODO for the ring.
///
/// On level 57 (the Pits), with the player in the arena and a critter as his pit fighter: A. a teleport (level 0) to a
/// square in the same quarter of the ring leaves the fight on. B. a teleport to a square outside the ring ends it.
/// </summary>
public sealed class PitsArenaCoward : Scenario
{
    public override string Name => "pits-arena-coward";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:196-218, 963-976, 2029-2055; uw2/src/sys/MISCUTIL.C:62-75";

    string _problem;
    bool _aFighting, _bFighting;
    int _bQuest129 = -1, _bQuest133 = -1, _bAttitude = -1, _bGoal = -1, _bSlot = -1;
    (int x, int y) _aFrom, _aTo, _bTo;

    /// <summary>mpos, uw2 MISCUTIL.C:62-75.</summary>
    static int Mpos(int x, int y)
    {
        if (Math.Abs(x) / 2 > Math.Abs(y)) return x > 0 ? 2 : 6;
        if (Math.Abs(y) / 2 > Math.Abs(x)) return y > 0 ? 0 : 4;
        if (x < 0) return (y > 0 ? 2 : 0) + 5;
        return (y < 0 ? 2 : 0) + 1;
    }

    /// <summary>in_arena, uw2 WORLDEV.C:963-976.</summary>
    static bool InArena(int x, int y)
    {
        int[] limits = { 0xF, 0x10, 0xE, 0xF };
        x -= 0x1F; y -= 0x1F;
        int q = Mpos(x, y) >> 1;
        x = Math.Abs(x); y = Math.Abs(y);
        return x >= 3 && y >= 3 && limits[q] >= x && limits[q] >= y;
    }

    static int Quarter(int x, int y) => Mpos(x - 0x1F, y - 0x1F) >> 1;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        Remains.GoToLevel(57);
        if (playerdat.dungeon_level != 57) { _problem = $"the move to level 57 left the player on level {playerdat.dungeon_level}"; return; }
        var p = playerdat.playerObject;
        // put the player on an arena square where find_good_x_and_y keeps him
        bool placed = false;
        for (int x = 0x1F - 0xE; x <= 0x1F + 0xE && !placed; x++)
            for (int y = 0x1F - 0xE; y <= 0x1F + 0xE && !placed; y++)
                if (InArena(x, y) && Teleportation.FindGoodXAndY(p, x, y, out int gx, out int gy) && gx == x && gy == y)
                {
                    Teleportation.Teleport(0, x, y, 57, 0);
                    Teleportation.HandleTeleportation();
                    placed = p.tileX == x && p.tileY == y;
                }
        if (!placed) { _problem = "no arena square on level 57 the player could be put on"; return; }
        _aFrom = (p.tileX, p.tileY);
        await c.Frames(2);

        // A: another square of the same quarter, at least 2 away.
        int q = Quarter(_aFrom.x, _aFrom.y);
        _aTo = (-1, -1);
        for (int x = 0; x < 64 && _aTo.x < 0; x++)
            for (int y = 0; y < 64 && _aTo.x < 0; y++)
                if (Math.Max(Math.Abs(x - _aFrom.x), Math.Abs(y - _aFrom.y)) >= 2 && InArena(x, y) && Quarter(x, y) == q
                    && Teleportation.FindGoodXAndY(p, x, y, out int gx, out int gy) && gx == x && gy == y)
                    _aTo = (x, y);
        // B: a square outside the ring.
        _bTo = (-1, -1);
        for (int x = 0; x < 64 && _bTo.x < 0; x++)
            for (int y = 0; y < 64 && _bTo.x < 0; y++)
                if (!InArena(x, y) && Teleportation.FindGoodXAndY(p, x, y, out int gx, out int gy) && gx == x && gy == y)
                    _bTo = (x, y);
        if (_aTo.x < 0 || _bTo.x < 0) { _problem = $"no square for A ({_aTo}) or B ({_bTo})"; return; }

        var fighter = BatchB.SpawnCritter(BatchB.FindCritter(id => true), _aFrom.x + (_aFrom.x < 0x1F ? 1 : -1), _aFrom.y);
        if (fighter == null) { _problem = "no fighter could be made"; return; }
        fighter.npc_attitude = 0;
        fighter.npc_goal = 5;
        void Fight()
        {
            playerdat.IsFightingInPit = true;
            playerdat.SetPitFighter(0, (byte)fighter.index);
            playerdat.SetQuest(129, 3);
            playerdat.SetQuest(133, 2);
        }

        Fight();
        Teleportation.Teleport(0, _aTo.x, _aTo.y, 0, 0);
        _aFighting = playerdat.IsFightingInPit;
        Teleportation.HandleTeleportation();
        await Remains.EndConversation(c);
        c.Note($"A: from {_aFrom} (quarter {q}) to {_aTo}: still fighting {_aFighting}; player at {p.tileX},{p.tileY}");

        Fight();
        fighter.npc_attitude = 0;
        fighter.npc_goal = 5;
        Teleportation.Teleport(0, _bTo.x, _bTo.y, 57, 0);
        _bFighting = playerdat.IsFightingInPit;
        _bQuest129 = playerdat.GetQuest(129);
        _bQuest133 = playerdat.GetQuest(133);
        _bAttitude = fighter.npc_attitude;
        _bGoal = fighter.npc_goal;
        _bSlot = playerdat.GetPitFighter(0);
        Teleportation.HandleTeleportation();
        c.Note($"B: to {_bTo} outside the ring: still fighting {_bFighting}, quests 129 {_bQuest129} 133 {_bQuest133}, fighter attitude {_bAttitude} goal {_bGoal}, slot {_bSlot}");
        await Remains.EndConversation(c);
        playerdat.IsFightingInPit = false;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_aFighting, $"A: a teleport within the same quarter of the arena ({_aFrom} to {_aTo}, level 0) ended the pit fight (uw2 WORLDEV.C:198-199, 212-218)");
        k.True(!_bFighting, $"B: a teleport out of the arena ring to {_bTo} left the pit fight on (in_arena: uw2 WORLDEV.C:212-218, 963-976)");
        k.Equal("B: pit record (quest 129) after running", 0, _bQuest129, "uw2 WORLDEV.C:2053");
        k.Equal("B: Jospur's debt (quest 133) after running", 0, _bQuest133, "uw2 WORLDEV.C:2054");
        k.Equal("B: fighter's attitude after running", 1, _bAttitude, "SET_ATTITUDE(warrior, 1): uw2 WORLDEV.C:2039");
        k.Equal("B: fighter's goal after running", 1, _bGoal, "SET_GOAL(warrior, 1): uw2 WORLDEV.C:2041");
        k.Equal("B: pit fighter slot after running", 0, _bSlot, "player->pit_fighters[first] = 0: uw2 WORLDEV.C:2050");
        return k.Result;
    }
}

/// <summary>
/// Jail (UW2), the parts after the term. put_player_in_jail draws the term's random part (rand() % 0x5A) first and
/// passes the time, calms the guards (race 0x1C), sets quest 112, and only then catches the critters up with
/// update_all_critters_whilst_player_snoozes, which draws a new heading for each critter; it moves Lord British
/// (whoami 0x8E) to 0x2A, 0x22 with home 0x28, 0x27, and opens the door on the cell's square 0x2A, 0x26 with
/// gruesome_door_hack (uw2/src/event/WORLDEV.C:2293-2313, GAMEWRAP.C:426-438).
///
/// The port's GoToJail (src/player/playerdatdeath.cs) caught the critters up first, so their headings took the draws
/// and the term came from a later one; and it closed an open door on
/// 0x2A, 0x26 instead of opening it.
///
/// On level 1 a closed door is put on 0x2A, 0x26 and GoToJail runs from seed 7: the term is exactly the one the
/// first draw gives, Lord British is on the port's 40, 35 with home 40, 38 (a deliberate choice), and the door is open and still.
/// </summary>
public sealed class JailCatchUp : Scenario
{
    const uint JailSeed = 7;

    public override string Name => "jail-lord-british-and-door";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/WORLDEV.C:2293-2313; uw2/src/game/GAMEWRAP.C:426-438";

    string _problem;
    int _lbX = -1, _lbY = -1, _lbQuality = -1, _lbOwner = -1, _doorId = -1, _hour;
    long _term;
    bool _doorMoving;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.dungeon_level != 1) _problem = $"the new game is on level {playerdat.dungeon_level}, not the castle";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var map = UWTileMap.current_tilemap;
        var doorObj = BatchB.SpawnStatic(0x140, 0x2A, 0x26);
        if (doorObj == null) { _problem = "no door could be made on 0x2A, 0x26"; return Task.CompletedTask; }
        doorObj.xpos = 3; doorObj.ypos = 3;

        var jail = typeof(playerdat).GetMethod("GoToJail", BindingFlags.NonPublic | BindingFlags.Static);
        if (jail == null) { _problem = "playerdat.GoToJail not found"; return Task.CompletedTask; }
        int clockBefore = playerdat.ClockValue;
        _hour = (clockBefore / 0x3C00) % 0x14;
        Rng.Source = new BorlandRandSource(JailSeed);
        jail.Invoke(null, null);
        _term = (long)(playerdat.ClockValue - clockBefore) >> 8;

        for (int i = 1; i < 256; i++)
        {
            var o = map.LevelObjects[i];
            if (o.majorclass == 1 && o.npc_whoami == 0x8E) { _lbX = o.tileX; _lbY = o.tileY; _lbQuality = o.quality; _lbOwner = o.owner; }
        }
        _doorId = doorObj.item_id;
        _doorMoving = door.isMoving(doorObj);
        c.Note($"term {_term} s at hour term {_hour}; LB at {_lbX},{_lbY} home {_lbQuality},{_lbOwner}; door now 0x{_doorId:X}, moving {_doorMoving}");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_lbX < 0) return "precondition: no Lord British (whoami 0x8E) on level 1";
        var k = new Checks();
        long expected = (0x28 - _hour) * 0x3C - (new BorlandRandSource(JailSeed).Rand() % 0x5A + 0x3C);
        k.Equal("jail term in seconds from the first draw (the critters catch up after it)", expected, _term, "pass_time before update_all_critters_whilst_player_snoozes: uw2 WORLDEV.C:2295, 2302");
        // The port keeps Lord British at 40,35 with home 40,38 (the original's 0x2A,0x22 and 0x28,0x27, uw2
        // WORLDEV.C:2306-2308), a deliberate choice so he is less likely to get stuck on a doorway.
        k.True(_lbX == 40 && _lbY == 35, $"Lord British expected on 40, 35, is on {_lbX}, {_lbY}");
        k.Equal("Lord British's home x", 40, _lbQuality, "the port's home x");
        k.Equal("Lord British's home y", 38, _lbOwner, "the port's home y");
        k.Equal("door on the cell's square", 0x148, _doorId, "gruesome_door_hack: OpenDoor: uw2 GAMEWRAP.C:434");
        k.True(!_doorMoving, "the cell's door expected to have finished opening (toast_animobj: uw2 GAMEWRAP.C:435-437)");
        return k.Result;
    }
}
