using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// enhancements.smoothcamera: the camera is drawn every frame between its last two places from the
/// motion update, and the game itself does not change (docs/enhancements.md).
///
/// The original places the camera once a drawn frame, after check_physics has run with the ticks
/// since the last frame (uw1/src/motion/PLAYMOVE.C:324-356, uw2 PLAYMOVE.C:317-349). The port runs
/// the motion update every 0.097659 s with a fixed 0x19 ticks (main._PhysicsProcess), every 5 or 6
/// physics frames at 60 a second as it carries the time left over, and placed the camera only then. This is drawing only, so the
/// rules are checked against the flag itself: the same walk with the flag on and off must give the
/// same game state at every update.
///
/// The scenario faces the player down a straight run (as guard-walk finds one) and holds W for 120
/// frames, twice from the same start, once with the flag on and once off, recording each update's
/// game state (playerMotionParams, CameraTileX/Y, CameraYawHeadingRelated_2B52, CameraPointer2C)
/// and each frame's drawn camera. With the flag on, each update must be drawn at several places on
/// the way from the place before it to its own, and the frame of an update must show the place
/// before it or no more than one physics frame's share of an update on from it (alpha 0 plus the
/// time the update carried over, under 1/60 s); a whole update (alpha 1) draws the update's place exactly. With it off,
/// every frame must show the latest update's place. It then checks two places drawn at once with
/// the flag on: a teleport to the next square, and a switch back from a camera that is not the
/// player's (do_trap_camera, roaming sight) half a tile away; and that a turn is drawn the short
/// way round past south. It notes the game clock's rise per second and the largest move of one
/// update seen, for the one-tile limit beyond which a move is never slid.
/// </summary>
public sealed class SmoothCamera : Scenario
{
    const int WalkFrames = 120;
    const int StopFrames = 90;   // the speed falls to 0 over 9 updates, 54 frames
    const double FirstFrameAlpha = (1.0 / 60) / 0.097659;   // one physics frame at --fixed-fps 60 over main.MotionUpdateInterval

    public override string Name => "smooth-camera";
    public override string Cites => "underworld-exhumed uw1/src/motion/PLAYMOVE.C:324-356, uw2/src/motion/PLAYMOVE.C:317-349 (the camera follows every frame); Godot main.cs _PhysicsProcess, playerdatcamera.cs PositionCamera";

    string _problem;
    int _dir, _sx, _sy;
    Run _on, _off;
    readonly List<string> _fails = new();
    readonly List<string> _notes = new();

    sealed class Run
    {
        public readonly List<string> States = new();
        public readonly List<(int Update, Vector3 Drawn, Vector3 From, Vector3 To, Vector3 Rot)> Frames = new();
        public int ClockRise, MaxStep;
    }

    static Vector3 Pos(cameraRender.Pose p) => cameraRender.GodotPosition(p.x, p.y, p.z);
    static Vector3 Drawn => main.cameraYawGimbal_world.Position;

    static string State() =>
        Convert.ToHexString(motion.playerMotionParams.data)
        + $" tile {playerdat.CameraTileX},{playerdat.CameraTileY} 2B52 {playerdat.CameraYawHeadingRelated_2B52} 2C {playerdat.CameraPointer2C}";

    public override async Task Setup(ScenarioContext c)
    {
        var camera = await Guards.CameraOnPlayer(c);
        var p = playerdat.playerObject;
        int best = int.MaxValue;
        _dir = -1;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                if (dd >= best) continue;
                var (d, _) = GuardWalk.BestRun(x, y);
                if (d >= 0) { best = dd; _sx = x; _sy = y; _dir = d; }
            }
        if (_dir < 0) { _problem = "no straight run of 4 or more open, empty squares on this level"; return; }
        if (!uwsettings.instance.enhancements.smoothcamera) { _problem = "smoothcamera is not on by default"; return; }
        c.Note($"{camera}; runs {"NESW"[_dir]} from ({_sx}, {_sy})");
    }

    async Task<bool> ToStart(ScenarioContext c)
    {
        if (!await Guards.TeleportHere(c, _sx, _sy)) return false;
        playerdat.PlayerCameraYaw_dseg_8294 = GuardWalk.Yaw[_dir];
        await c.Frames(30);
        return true;
    }

    async Task WaitForUpdate(ScenarioContext c)
    {
        int clock = playerdat.ClockValue;
        while (playerdat.ClockValue == clock) await c.Frames(1);
    }

    async Task<Run> Walk(ScenarioContext c, bool smooth)
    {
        uwsettings.instance.enhancements.smoothcamera = smooth;
        if (!await ToStart(c)) { _problem = $"the move to ({_sx}, {_sy}) did not happen"; return null; }
        Rng.Source = new BorlandRandSource(Seed);
        var run = new Run();
        await WaitForUpdate(c);
        int clock0 = playerdat.ClockValue, update = 0, clock = clock0;
        var last = cameraRender.To;
        Guards.Key(Key.W, true);
        for (int f = 0; f < WalkFrames + StopFrames; f++)
        {
            if (f == WalkFrames) Guards.Key(Key.W, false);
            await c.Frames(1);
            if (playerdat.ClockValue != clock)
            {
                clock = playerdat.ClockValue;
                update++;
                run.States.Add(State());
                var to = cameraRender.To;
                run.MaxStep = Math.Max(run.MaxStep, Math.Max(Math.Abs(to.x - last.x), Math.Max(Math.Abs(to.y - last.y), Math.Abs(to.z - last.z))));
                last = to;
            }
            run.Frames.Add((update, Drawn, Pos(cameraRender.From), Pos(cameraRender.To), main.cameraYawGimbal_world.Rotation));
        }
        run.ClockRise = playerdat.ClockValue - clock0;
        return run;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        try
        {
            _on = await Walk(c, true);
            if (_on == null) return;
            // alpha 1 draws the update's place exactly
            cameraRender.DrawBetween(1);
            if (Drawn != Pos(cameraRender.To)) _fails.Add($"alpha 1 drew {Drawn}, not the update's place {Pos(cameraRender.To)}");
            await TurnPastSouth(c);
            await TeleportSnap(c);
            await CameraSwitchSnap(c);
            await Fall(c);
            _off = await Walk(c, false);
        }
        finally
        {
            Guards.ReleaseHeldKeys();
            uwsettings.instance.enhancements.smoothcamera = true;
        }
    }

    /// <summary>A turn from just west of south to just east of it is drawn through south, not round through north.</summary>
    async Task TurnPastSouth(ScenarioContext c)
    {
        await c.Frames(1);
        cameraRender.From.yaw = 0x7F00;
        cameraRender.To.yaw = unchecked((short)0x8100);
        cameraRender.DrawBetween(0.5);
        // yaw 0x8000 is south: the gimbal is turned by PI and then by PI again, so it faces as at yaw 0 turned round
        var forward = main.cameraYawGimbal_world.Basis.Z;
        var expected = Basis.Identity.Rotated(Vector3.Up, Mathf.Pi).Rotated(Vector3.Up, -Mathf.Pi * 32768f / 32767f).Z;
        if (forward.DistanceTo(expected) > 0.01f) _fails.Add($"halfway from yaw 0x7F00 to 0x8100 drew facing {forward}, not south {expected}");
        playerdat.PositionPlayerCamera();
        await c.Frames(1);
    }

    /// <summary>A teleport to the next square is drawn at once on the next frame.</summary>
    async Task TeleportSnap(ScenarioContext c)
    {
        if (!await ToStart(c)) { _fails.Add("precondition: could not return to the start for the teleport"); return; }
        var before = Drawn;
        int tx = _sx + GuardWalk.Dx[_dir], ty = _sy + GuardWalk.Dy[_dir];
        Teleportation.Teleport(character: 0, tileX: tx, tileY: ty, newLevel: 0, HeadingHeightFlag: 0);
        bool moved = false;
        for (int f = 0; f < 12; f++)
        {
            await c.Frames(1);
            var d = Drawn;
            if (d == before) continue;
            var to = Pos(cameraRender.To);
            if (d != to) { _fails.Add($"frame {f} after the teleport drew {d}, between the old place {before} and the new {to}"); return; }
            moved = true;
        }
        if (!moved) _fails.Add("precondition: the teleport did not move the camera");
        else _notes.Add($"teleport drawn at once, {before.DistanceTo(Drawn):0.00} away");
    }

    /// <summary>Back from a camera that is not the player's, the player's camera is drawn at once even when near.</summary>
    async Task CameraSwitchSnap(ScenarioContext c)
    {
        await WaitForUpdate(c);
        var player = cameraRender.To;
        playerdat.DoCameraX = (short)(player.x + 0x80);
        playerdat.DoCameraY = player.y;
        playerdat.DoCameraZ = player.z;
        playerdat.DoCameraH = player.yaw;
        playerdat.DoCameraPitch = 0;
        playerdat.DoCameraRoll = 0;
        playerdat.CameraReference = null;
        await c.Frames(13);
        var away = Drawn;
        var doCam = cameraRender.GodotPosition(playerdat.DoCameraX, playerdat.DoCameraY, playerdat.DoCameraZ);
        if (away != doCam) _fails.Add($"the other camera was drawn at {away}, not its place {doCam}");
        playerdat.CameraReference = playerdat.playerObject;
        for (int f = 0; f < 12; f++)
        {
            await c.Frames(1);
            var d = Drawn;
            if (d == away) continue;
            var to = Pos(cameraRender.To);
            if (d != to) { _fails.Add($"frame {f} back on the player drew {d}, between the other camera {away} and the player {to}"); return; }
        }
        if (Drawn == away) _fails.Add("precondition: the camera never came back to the player");
    }

    /// <summary>For the note only: the largest drop of one update in a fall from near the ceiling.</summary>
    async Task Fall(ScenarioContext c)
    {
        if (!await ToStart(c)) return;
        await WaitForUpdate(c);
        int floor = motion.playerMotionParams.z_4;
        motion.playerMotionParams.z_4 = (short)Math.Min(floor + 0x300, 0x380);
        motion.playerMotionParams.gravity_10_Z = -4;   // as stepping off a ledge starts a fall
        motion.PlayerMotionUpdateRequired_dseg_D3 = true;
        await WaitForUpdate(c);
        int clock = playerdat.ClockValue, step = 0, top = cameraRender.To.z, lastZ = top;
        for (int f = 0; f < 240; f++)
        {
            await c.Frames(1);
            if (playerdat.ClockValue == clock) continue;
            clock = playerdat.ClockValue;
            step = Math.Max(step, lastZ - cameraRender.To.z);
            lastZ = cameraRender.To.z;
        }
        _notes.Add($"fall from camera z {top:X} to {lastZ:X}: largest drop of one update {step:X}");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_on == null || _off == null) return "precondition: a walk did not run; " + string.Join("; ", _fails);
        var k = new Checks();
        foreach (var f in _fails) k.True(false, f);

        k.True(_on.States.Count == _off.States.Count, $"{_on.States.Count} updates with the flag on, {_off.States.Count} off");
        int firstDiff = Enumerable.Range(0, Math.Min(_on.States.Count, _off.States.Count)).FirstOrDefault(i => _on.States[i] != _off.States[i], -1);
        k.True(firstDiff < 0, firstDiff < 0 ? "" : $"game state differs at update {firstDiff}: on {_on.States[firstDiff]}, off {_off.States[firstDiff]}");

        // flag on: each update that moved the camera drawn at several places on the line from the place before it, its own frame at alpha 0
        int moving = 0, fewest = int.MaxValue, outside = 0, notAtStart = 0;
        for (int u = 2; u < _on.States.Count; u++)   // the last update's frames are cut short by the end of the walk
        {
            var frames = _on.Frames.Where(f => f.Update == u).ToList();
            var from = _on.Frames.Last(f => f.Update == u - 1).To;
            var to = frames[0].To;
            if (from == to) continue;
            moving++;
            fewest = Math.Min(fewest, frames.Select(f => f.Drawn).Distinct().Count());
            var first = (frames[0].Drawn - from).Dot(to - from) / (to - from).LengthSquared();
            if (first < -1e-4f || first > FirstFrameAlpha + 1e-4f) notAtStart++;
            foreach (var f in frames)
            {
                var seg = to - from;
                float a = (f.Drawn - from).Dot(seg) / seg.LengthSquared();
                if (a < -1e-4f || a > 1 + 1e-4f || (from + seg * a).DistanceTo(f.Drawn) > 1e-3f) outside++;
            }
        }
        k.True(moving >= 5, $"precondition: only {moving} updates moved the camera with the flag on");
        k.True(fewest >= 3, $"an update that moved the camera was drawn at only {fewest} places");
        k.Equal("frames drawn off the line from an update's place before to its own", 0, outside, "cameraRender.DrawBetween");
        k.Equal("updates whose own frame was not drawn within one frame's share of the place before them (alpha 0 to 0.17)", 0, notAtStart, "main._Process");
        var lastOn = _on.Frames[^1];
        k.True(lastOn.Drawn == lastOn.To, $"standing still the flag-on camera drew {lastOn.Drawn}, not the update's place {lastOn.To}");

        // flag off: every frame shows the latest update's place, as before
        int offDiff = _off.Frames.Count(f => f.Drawn != f.To);
        k.Equal("flag-off frames not at the latest update's place", 0, offDiff, "playerdat.PositionCamera as before");
        k.True(_off.Frames.Select(f => f.Drawn).Distinct().Count() > 5, "precondition: the flag-off walk did not move the camera");

        c.Note($"on: {_on.States.Count} updates, fewest places per moving update {fewest}, largest move of one update {_on.MaxStep:X}; clock +{_on.ClockRise} over {WalkFrames + StopFrames} frames ({_on.ClockRise * 60.0 / (WalkFrames + StopFrames):0.0} a second against 256)");
        foreach (var n in _notes) c.Note(n);
        return k.Result;
    }
}
