using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Runs gameplay scenarios inside the real game, one game per process:
///
///   Godot --headless --fixed-fps 60 --path . res://tests/godot/scenarios/scenario_runner.tscn \
///     -- --game uw1|uw2 --scenarios a,b|--all --temp-home TMP [--uw1-dir D] [--uw2-dir D]
///
/// Run it through tests/scenarios.sh, which gives Godot a temporary HOME and passes it as
/// --temp-home. The runner refuses to run unless Godot's user data folder is inside --temp-home,
/// so it cannot touch the player's own settings.json, and it runs the game from a temporary game
/// folder it builds there (TempGameFolder), so a save cannot land in the real game folder.
///
/// Per scenario: point the game at the data the way LaunchMenu.OnLaunchInput does (without
/// its Save), install a seeded BorlandRandSource, instantiate Underworld.tscn, stop the intro
/// cutscene that main.StartGame queues, start a new game the way main.LaunchUWDemo does
/// (playerdat.InitEmptyPlayer, the demo's stand-in stats, uimanager.JourneyOnwards("DATA")),
/// then Setup, Act, Assert.
/// Prints "SCENARIO game/name PASS|FAIL reason|SKIP reason" per scenario and a final
/// "GODOT-TEST-VERDICT PASS|FAIL|SKIP". Godot's exit status is not to be trusted; the lines are
/// the result.
/// </summary>
public partial class ScenarioRunner : Node
{
    /// <summary>Every scenario. InAll false: run only when named (the harness's negative control).</summary>
    static readonly (Func<Scenario> Make, bool InAll)[] Registry =
    {
        (() => new ClockRuns(), true),
        (() => new SkillCheckSeeded(), true),
        (() => new NewGameStart(), true),
        (() => new NewGameRecord(), true),
        (() => new ExperienceLoss(), true),
        (() => new ExperienceGain(), true),
        (() => new SleepPassesTime(), true),
        (() => new GaramonBurial(), true),
        (() => new GaramonDeathClearsTalismans(), true),
        // batch A: combat and critters
        (() => new SwingChargeResets(), true),
        (() => new CritterMeleeChoice(), true),
        (() => new NpcCastNeedsSlot(), true),
        (() => new NpcShotFrameFour(), true),
        (() => new NpcMeleeFrameFour(), true),
        (() => new FleeingCritterSteers(), true),
        (() => new CritterFlees(), true),
        (() => new CritterAttackFrames(), true),
        (() => new AttackFindTarget(), true),
        (() => new AlliesJoinFight(), true),
        (() => new AlliesWearOff(), true),
        (() => new WanderingMonsters(), true),
        (() => new SpecialDeathsUW2(), true),
        (() => new PoisonWeaponDamage(), true),
        (() => new CombatEquipmentWear(), true),
        (() => new CritterLineOfSight(), true),
        (() => new PathsFreedOnLevelChange(), true),
        (() => new Uw1NpcSequences(), true),
        (() => new CritterFollow(), true),
        // batch B: player and magic rules
        (() => new SmiteUndeadRule(), true),
        (() => new SmiteFloatSkull(), true),
        (() => new KillExperience(), true),
        (() => new SkillPointGain(), true),
        (() => new LoreSkillRecord(), true),
        (() => new QuestFlagOperations(), true),
        (() => new NaturalHealing(), true),
        (() => new PlayerArmour(), true),
        (() => new ItemProtection(), true),
        (() => new SleepNight(), true),
        (() => new SleepKillornCrash(), true),
        (() => new SleepInVoid(), true),
        (() => new Uw1SleepMessages(), true),
        (() => new DreamChoice(), true),
        (() => new MapAreaRadius(), true),
        (() => new BritanniaSpellCircle(), true),
        (() => new AcademyManaLevel(), true),
        (() => new SlowTickInterval(), true),
        (() => new TargetedSpellMana(), true),
        (() => new TargetedSpellWaived(), true),
        (() => new Uw1CastingCheck(), true),
        (() => new SummonMonsterLevel(), true),
        (() => new CarryCapacity(), true),
        (() => new CollisionMapEdge(), true),
        (() => new VisionMapEdge(), true),
        (() => new PlayerComputeMana(), true),
        (() => new LightBurnRates(), true),
        (() => new LightOnQuality(), true),
        (() => new LightStackInSlot(), true),
        (() => new RepairCriticalFailure(), true),
        (() => new Uw1CreateObjectTrap(), true),
        (() => new Uw1SoundsDatLength(), true),
        (() => new Uw1AreaSpells(), true),
        (() => new AreaSpellArea(), true),
        (() => new Uw1Class7Ahead(), true),
        (() => new SpellRefusedNoMagic(), true),
        (() => new MindSpells(), true),
        (() => new CreatedObjectFineY(), true),
        // end batch B
        // batch C
        (() => new TimeOfDayHourlyStep(), true),
        (() => new NewGameTimeOfDay(), true),
        (() => new RepairTime(), true),
        (() => new RepairFate(), true),
        (() => new ObjectGarbageCollect(), true),
        (() => new PickupSplitFull(), true),
        (() => new PickupSplitCopy(), true),
        (() => new PickupWeight(), true),
        (() => new TheftOwnerMask(), true),
        (() => new JailTerm(), true),
        (() => new AltarasWand(), true),
        (() => new ScdSetQuestBit(), true),
        (() => new ScdBlockWriteBack(), true),
        (() => new ScdHourlyStepWriteBack(), true),
        (() => new QbertGate(), true),
        (() => new CastleSchedule(), true),
        (() => new HackSetAllClocks(), true),
        (() => new EnchantWeaponTier(), true),
        (() => new EnchantRechargeWand(), true),
        (() => new GivePtrNpc(), true),
        (() => new GiveMergeCoins(), true),
        (() => new GiveToNpcAllOrNone(), true),
        (() => new EndBarter(), true),
        (() => new BarterNearPut(), true),
        (() => new BarterAcceptDeal(), true),
        (() => new BarterAssessValue(), true),
        (() => new BarterInit(), true),
        (() => new BarterSlotsAndValuation(), true),
        (() => new BarterSetupAndDemand(), true),
        (() => new TakeFromNpcNear(), true),
        (() => new TrapChainFlow(), true),
        (() => new TeleportOccupied(), true),
        (() => new TeleportCritter(), true),
        (() => new SleepAmbush(), true),
        (() => new CritterGoesHome(), true),
        (() => new MissImpactSound(), true),
        (() => new NewPlayerPos(), true),
        (() => new WallHitSound(), true),
        (() => new CanPlace(), true),
        (() => new WeaponEachSwing(), true),
        (() => new MissileBodyLocation(), true),
        (() => new SilverTreeResurrection(), true),
        (() => new GemResurrection(), true),
        (() => new VoidAutomap(), true),
        (() => new PlayerLost(), true),
        (() => new TybalTalkTrap(), true),
        (() => new XTrapsValueRange(), true),
        (() => new NumberedVariableRanges(), true),
        (() => new MenuMusicRepeat(), true),
        (() => new Uw1EndgameEffects(), true),
        (() => new RingSlotRule(), true),
        (() => new CastleScheduleWhoami8F(), true),
        (() => new CreateObjectTrapPutAt(), true),
        (() => new Uw1SetVariableQuestBit(), true),
        (() => new MessagesAndSounds(), true),
        (() => new DragonChoice(), true),
        (() => new AltarasWandCutLine(), true),
        (() => new RepelUndeadSquare(), true),
        (() => new RepairDebrisAndCastMessages(), true),
        // batch D: guards
        (() => new GuardEat(), true),
        (() => new GuardLight(), true),
        (() => new GuardPickupDrop(), true),
        (() => new GuardDoor(), true),
        (() => new GuardSwitch(), true),
        (() => new GuardTalk(), true),
        (() => new GuardWalk(), true),
        (() => new GuardLevelChange(), true),
        (() => new GuardSaveLoad(), true),
        (() => new GuardMelee(), true),
        // issue #69
        (() => new Uw2AutomapSaved(), true),
        // issue #120
        (() => new ObjectCastCooldown(), true),
        // issue #117
        (() => new DoCollisionEntry(), true),
        // issue #207
        (() => new CulledContainerContents(), true),
        (() => new MusicThemeChoice(), true),
        (() => new DoCollisionMomentumRestore(), true),
        // fix/mana-and-food
        (() => new SkillAdvanceMana(), true),
        (() => new MushroomMana(), true),
        (() => new FoodHeal(), true),
        (() => new FoodClearsCastWait(), true),
        // edge cases
        (() => new RaceAttitudeRange(), true),
        (() => new CollisionScanLimit(), true),
        (() => new DemoFallbackRecompute(), true),
        // death, remains and the Pits arena
        (() => new DeathDropsCursorObject(), true),
        (() => new CritterRemainsPutAt(), true),
        (() => new BoulderBreaks(), true),
        (() => new TombFloatSkulls(), true),
        (() => new PitsArenaCoward(), true),
        (() => new JailCatchUp(), true),
        // missing routines: doors
        (() => new DoorTurnsRound(), true),
        (() => new DoorObstructed(), true),
        (() => new GruesomeDoorHack(), true),
        // missing routines: stopping sounds and cutscene speech
        (() => new WaterSoundStops(), true),
        (() => new EffectsStopSoundOff(), true),
        (() => new MapStopsEffects(), true),
        (() => new CutsceneSpeechStops(), true),
        (() => new CutsceneLoop(), true),
        // missing routines
        (() => new StepMoves(), true),
        // missing routines: reach
        (() => new ReachBlockingTerrain(), true),
        (() => new MoonstoneInContainer(), true),
        (() => new LightSphereInContainer(), true),
        (() => new BonesLookScenario(), true),
        (() => new TalismanLookScenario(), true),
        (() => new BonesBurial(), true),
        (() => new LastTalismanKeepsHp(), true),
        (() => new CritterLockedDoor(), true),
        // visual effects
        (() => new WeaponSway(), true),
        (() => new MenuPaletteCycle(), true),
        (() => new StartupIntro(), true),
        (() => new MainMenuKeys(), true),
        (() => new OptionsKeys(), true),
        (() => new OptionShortcuts(), true),
        (() => new OptionsMouseRelease(), true),
        (() => new HarnessFails(), false),
    };

    const string GameScene = "res://scenes/Underworld.tscn";

    /// <summary>Wall-clock limit for the intro cutscene to stop. Its splash waits are real time (Task.Delay).</summary>
    const ulong CutsceneStopMs = 20000;

    int _pass, _fail, _skip;
    string _game;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    void Line(string game, string name, string verdict)
    {
        if (verdict.StartsWith("PASS")) _pass++;
        else if (verdict.StartsWith("SKIP")) _skip++;
        else _fail++;
        GD.Print($"SCENARIO {game}/{name} {verdict}");
    }

    async void Run()
    {
        try
        {
            await RunAll();
        }
        catch (Exception ex)
        {
            // Its own line, so a failure outside any scenario cannot hide behind a scenario
            // that KNOWN_FAILURES lists. "(runner)" is not a scenario name and cannot be listed.
            GD.PrintErr("runner failed: " + ex);
            Line(_game ?? "?", "(runner)", "FAIL " + (ex.GetType().Name + ": " + ex.Message).Split('\n')[0]);
        }
        string overall = _fail > 0 ? "FAIL" : _pass > 0 ? "PASS" : "SKIP";
        GD.Print($"GODOT-TEST-VERDICT {overall}");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    async Task RunAll()
    {
        var args = ParseArgs(OS.GetCmdlineUserArgs());
        string game = args.GetValueOrDefault("--game", "");
        _game = game;
        if (game != "uw1" && game != "uw2")
        {
            _fail++;
            GD.PrintErr("usage: -- --game uw1|uw2 --scenarios a,b|--all --temp-home DIR [--uw1-dir DIR] [--uw2-dir DIR]");
            return;
        }

        // Which scenarios. An unknown name is a failure, not silence.
        var chosen = new List<Scenario>();
        if (args.ContainsKey("--all"))
        {
            chosen.AddRange(Registry.Where(r => r.InAll).Select(r => r.Make()));
        }
        foreach (string n in args.GetValueOrDefault("--scenarios", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var found = Registry.Select(r => r.Make()).FirstOrDefault(s => s.Name == n);
            if (found == null) Line(game, n, "FAIL no such scenario");
            else if (!chosen.Any(s => s.Name == n)) chosen.Add(found);
        }

        // What this run will report on. tests/scenarios.sh checks that every one of these
        // gets a SCENARIO line, so a scenario that silently never ran fails the run.
        GD.Print("SCENARIO-PLAN " + string.Join(",", chosen.Select(s => s.Name)));

        var applicable = new List<Scenario>();
        foreach (var s in chosen)
        {
            if (s.Games.Contains(game)) applicable.Add(s);
            else Line(game, s.Name, $"SKIP not a {game} scenario ({string.Join(", ", s.Games)} only)");
        }
        if (applicable.Count == 0) return;

        // Run only inside the temporary home: Godot's user data folder (settings.json, logs,
        // screenshots) must be in it. A positive test, so a platform where Godot puts that folder
        // somewhere the script did not redirect is refused rather than trusted.
        string tempHome = args.GetValueOrDefault("--temp-home", "");
        string userDir = OS.GetUserDataDir();
        if (string.IsNullOrEmpty(tempHome) || !IsUnder(userDir, tempHome))
        {
            foreach (var s in applicable)
                Line(game, s.Name, $"FAIL not isolated: Godot's user data folder is not inside --temp-home ({(string.IsNullOrEmpty(tempHome) ? "none given" : tempHome)}); run through tests/scenarios.sh");
            return;
        }

        string realDir = args.GetValueOrDefault($"--{game}-dir", "");
        if (string.IsNullOrEmpty(realDir))
        {
            foreach (var s in applicable)
                Line(game, s.Name, $"SKIP no game folder: pass --{game}-dir, set {game.ToUpperInvariant()}_DIR, or set path{game} in the game's settings");
            return;
        }
        if (!Directory.Exists(Path.Combine(realDir, "DATA")))
        {
            foreach (var s in applicable)
                Line(game, s.Name, $"SKIP no game data: {realDir} has no DATA folder");
            return;
        }
        string gameDir;
        try
        {
            gameDir = TempGameFolder(realDir, Path.Combine(tempHome, "game-" + game));
        }
        catch (Exception ex)
        {
            foreach (var s in applicable)
                Line(game, s.Name, $"FAIL could not build the temporary game folder: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return;
        }

        // As LaunchMenu.OnLaunchInput does, minus Save(): the settings stay in memory. Touch
        // uwsettings first so its static constructor (LoadSettings, which sets _RES and
        // BasePath from the file) has run and cannot overwrite these afterwards.
        _ = uwsettings.instance;
        if (game == "uw1")
        {
            uwsettings.instance.pathuw1 = gameDir;
            uwsettings.instance.gametoload = "UW1";
            UWClass._RES = UWClass.GAME_UW1;
        }
        else
        {
            uwsettings.instance.pathuw2 = gameDir;
            uwsettings.instance.gametoload = "UW2";
            UWClass._RES = UWClass.GAME_UW2;
        }
        UWClass.BasePath = gameDir;

        var packed = GD.Load<PackedScene>(GameScene);
        if (packed == null)
        {
            foreach (var s in applicable) Line(game, s.Name, $"FAIL {GameScene} did not load");
            return;
        }

        foreach (var s in applicable)
        {
            Line(game, s.Name, await RunOne(s, game, packed));
        }
    }

    async Task<string> RunOne(Scenario s, string game, PackedScene packed)
    {
        GD.Print($"  {game}/{s.Name}: {s.Cites}");
        var previousSource = Rng.Source;
        Node root = null;
        try
        {
            Rng.Source = new BorlandRandSource(s.Seed);
            ResetProcessStatics();

            // The value a fresh process starts with. A previous scenario left it at GAME, and
            // the intro cutscene restores whatever mode it found when it ends.
            uimanager.CurrentGameMode = uimanager.GameModes.CUTSCENE;

            root = packed.Instantiate();
            root.Name = "Underworld";   // the game looks itself up at /root/Underworld
            GetTree().Root.AddChild(root);   // uimanager._Ready runs main.StartGame here

            if (root is not UnderworldRoot uwRoot)
                return "FAIL Underworld.tscn's root is not an UnderworldRoot";
            if (!uwRoot.FontsReady)
                return $"FAIL the game did not start: fonts not loaded ({uwRoot.FontError ?? "no reason"})";

            string notStarted = await StartNewGame();
            if (notStarted != null) return "FAIL " + notStarted;

            // A "more" prompt left open keeps uimanager.blockinput true, and with it the player's
            // update (PlayerTimedLoop) does nothing. The flag is static (MessageDisplay.cs:46) and a
            // new game does not clear it. It is cleared here, after the new game has started, not
            // in ResetProcessStatics: a conversation an earlier scenario started keeps running
            // across the scene change and its pause() (conversation_functions/pause.cs:10) can set
            // the flag again while this new game starts.
            MessageDisplay.WaitingForMore = false;

            // Seeded again, so Setup starts from exactly Seed: the seeding above makes the new
            // game's own draws repeatable, and they have moved the generator on since.
            Rng.Source = new BorlandRandSource(s.Seed);

            var c = new ScenarioContext(this, game, root);
            await s.Setup(c);
            await s.Act(c);
            string reason = s.Assert(c);
            return reason == null ? "PASS" : "FAIL " + reason;
        }
        catch (Exception ex)
        {
            string first = (ex.GetType().Name + ": " + ex.Message).Split('\n')[0];
            GD.PrintErr($"{game}/{s.Name} threw: {ex}");
            return "FAIL " + first;
        }
        finally
        {
            Rng.Source = previousSource;
            Guards.ReleaseHeldKeys();
            if (root != null && IsInstanceValid(root))
            {
                await DrainAudio();
                root.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }

    /// <summary>
    /// Puts back the statics a fresh process starts with that a new game does not reset and a
    /// scenario may have moved: the player loop's tick counters (playerdatloop.cs:13-17; only
    /// Load resets one of them) and the critters' free-path mask (npcai.cs:185, set only at
    /// start-up), and the clock of the last spell cast (runicmagic.cs, reset by DOS only on a
    /// restore or a death). Without this a scenario's 20-second and 5-minute ticks fall where earlier
    /// scenarios left them, pathfinding runs with the paths an earlier scenario took, and a cast can
    /// be refused as too soon after an earlier scenario's.
    /// start-up). Without this a scenario's 20-second and 5-minute ticks fall where earlier
    /// scenarios left them, and pathfinding runs with the paths an earlier scenario took.
    /// Also the palette number (palette.cs) and its colour-cycle steps (paletteloader.cs:36-37): a scenario that ends
    /// the game or goes to the main menu leaves palette 2 and a step of its longer menu cycle, and main.StartGame's
    /// switch back to palette 0 then indexes past palette 0's cycle (paletteloader.cs:431) and stops the next game
    /// starting. The field is set directly, so the reset does not touch the shader.
    /// </summary>
    static void ResetProcessStatics()
    {
        SetStatic(typeof(Palette), "_currentpalette", 0);
        SetStatic(typeof(playerdat), "playertimer", 0.0);
        SetStatic(typeof(playerdat), "playerUpdateCounter", 0);
        SetStatic(typeof(playerdat), "secondcounter", 0);
        SetStatic(typeof(npc), "BitFieldForPathing_dseg_67d6_B4", 0xFFFF);
        SetStatic(typeof(RunicMagic), "LastSpellCastClock_dseg_5c99_16E3", 0);
        PaletteLoader.NextPaletteCycle_GAME = -1;
        PaletteLoader.NextPaletteCycle_UI = -1;
    }

    static void SetStatic(Type type, string name, object value)
    {
        var field = type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            ?? throw new MissingFieldException(type.Name, name);
        field.SetValue(null, value);
    }

    /// <summary>The cutscenes the game played at start-up, in order, before the new game.</summary>
    public static readonly System.Collections.Generic.List<int> StartupCutscenes = new();

    /// <summary>
    /// Whether the temporary game folder held a saved game at start-up. It starts empty, but a
    /// scenario that saves leaves its slot for the later ones.
    /// </summary>
    public static bool StartupHadSaves;

    /// <summary>
    /// From the scene just added to "in game on level 1": stop the title cutscene that
    /// main.StartGame queued and the introduction its callback (uimanager.TitleFinished) plays
    /// when there is no saved game, wait for the main menu, then make the calls
    /// main.LaunchUWDemo makes. Returns null or why it failed.
    /// </summary>
    async Task<string> StartNewGame()
    {
        // The title (cutscene 9) and, with no saved game in the temporary game folder, the
        // introduction after it (uimanager.TitleFinished): each is stopped as it starts, and its
        // number recorded for startup-intro.
        StartupCutscenes.Clear();
        StartupHadSaves = uimanager.AnySavedGames();
        ulong deadline = Time.GetTicksMsec() + CutsceneStopMs;
        while (cutsplayer.IsPlaying || uimanager.CurrentGameMode != uimanager.GameModes.MAIN)
        {
            if (Time.GetTicksMsec() > deadline)
                return $"the intro cutscene did not stop (mode {uimanager.CurrentGameMode}, playing {cutsplayer.IsPlaying}, stopped {string.Join(", ", StartupCutscenes)}, last {cutsplayer.CurrentCutsceneNo})";
            if (cutsplayer.IsPlaying
                && (StartupCutscenes.Count == 0 || StartupCutscenes[^1] != cutsplayer.CurrentCutsceneNo))
            {
                StartupCutscenes.Add(cutsplayer.CurrentCutsceneNo);
                cutsplayer.StopCutscene();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        uimanager.EnableDisable(uimanager.instance.PanelMainMenu, false);
        playerdat.InitEmptyPlayer();
        // InitEmptyPlayer leaves max_hp and play_hp at 0 and the attributes unset, and with
        // those alone the Avatar dies on the first PlayerTimedLoop. These are the values
        // main.LaunchUWDemo itself gives a demo with no DPLAYER file (main.cs:216-222), the
        // game's own stand-in for chargen, followed by its player_compute(1) recompute of the
        // maxima, the mana and the carrying limit. The chargen screens would roll real ones.
        playerdat.CharName = "DEMO";
        playerdat.play_hp = 30;
        playerdat.STR = 20;
        playerdat.INT = 20;
        playerdat.DEX = 20;
        // The carrying limit comes from player_compute (RecalculateHPManaMaxWeight), which the
        // stand-in never runs: computed only inside InitEmptyPlayer, at Strength 0, it is 0 in UW1
        // (Strength * 20, uw1 SKILLS.C:112). Run it for the stand-in Strength, keeping the
        // stand-in's hit points and mana.
        int standInMaxMana = playerdat.max_mana;
        playerdat.RecalculateHPManaMaxWeight(false);
        playerdat.max_hp = 30;
        playerdat.max_mana = standInMaxMana;
        uimanager.instance.JourneyOnwards("DATA");
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        if (!uimanager.InGame) return $"the new game is not in game (mode {uimanager.CurrentGameMode})";
        if (playerdat.dungeon_level != 1) return $"the new game is on level {playerdat.dungeon_level}, not 1";
        if (playerdat.play_hp <= 0) return $"the new game's Avatar has {playerdat.play_hp} hit points";
        return null;
    }

    /// <summary>
    /// Lets the AudioServer let go of the game's music and sound playbacks before the scene is
    /// freed, as UnderworldRoot.DrainAudioThenQuit does on the game's own quit path. Freeing the
    /// scene without this leaves a playback the server still owns, and Godot 4.3 then hangs on
    /// shutdown (issue #78): measured here, the process never exited after its verdict.
    /// The wait is real time (Task.Delay), not a scene timer, because under --fixed-fps a
    /// timer counts game time and frames run faster than real time.
    /// </summary>
    async Task DrainAudio()
    {
        var music = MusicStreamPlayer.Instance;
        var sfx = Sfx.SfxStreamPlayer.Instance;
        bool stopped = false;
        for (int attempt = 0; !stopped && attempt < 4; attempt++)
        {
            if (attempt > 0) await Task.Delay(100);
            int join = attempt == 0 ? 500 : 50;
            bool m = music == null || !IsInstanceValid(music) || music.BeginGodotAudioShutdown(join);
            bool f = sfx == null || !IsInstanceValid(sfx) || sfx.BeginGodotAudioShutdown(join);
            stopped = m && f;
        }
        await Task.Delay(250);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!stopped)
        {
            GD.PushError("Audio producers did not stop; leaving their objects alone.");
            return;
        }
        if (music != null && IsInstanceValid(music)) music.ReleaseGodotAudioBindings();
        if (sfx != null && IsInstanceValid(sfx)) sfx.ReleaseGodotAudioBindings();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// Builds the folder the game runs from: a real directory holding a symbolic link to each
    /// entry of the real game folder, except the save slots (SAVE0 to SAVE9, and anything else
    /// whose name starts SAVE), which are empty real directories. The game writes saves into
    /// BasePath/SAVEn and its transaction files beside them (src/savegame/SaveGame.cs,
    /// SlotTransaction.cs), so with BasePath here a save stays in the temporary home; the shipped
    /// data is read through the links and never copied. Symbolic links need no privilege on
    /// macOS and Linux; on Windows they need Developer Mode, and without it this throws and every
    /// scenario fails rather than run against the real folder.
    /// </summary>
    static string TempGameFolder(string realDir, string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(realDir))
        {
            string name = Path.GetFileName(entry);
            string target = Path.Combine(dir, name);
            if (name.StartsWith("SAVE", StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(entry)) Directory.CreateDirectory(target);
                continue;   // a stray SAVE file is left out: nothing of the player's is reachable
            }
            if (Directory.Exists(entry)) Directory.CreateSymbolicLink(target, entry);
            else File.CreateSymbolicLink(target, entry);
        }
        for (int n = 0; n <= 4; n++) Directory.CreateDirectory(Path.Combine(dir, $"SAVE{n}"));
        return dir;
    }

    /// <summary>Whether path is inside dir, comparing separators and case loosely (Windows, macOS).</summary>
    static bool IsUnder(string path, string dir)
    {
        static string Norm(string x) => Path.GetFullPath(x).Replace('\\', '/').TrimEnd('/').ToLowerInvariant() + "/";
        return Norm(path).StartsWith(Norm(dir), StringComparison.Ordinal);
    }

    /// <summary>"--key value" pairs; a flag with no value (e.g. --all) maps to "".</summary>
    static Dictionary<string, string> ParseArgs(string[] argv)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) d[argv[i]] = argv[++i];
            else d[argv[i]] = "";
        }
        return d;
    }
}

/// <summary>
/// The harness's own negative control: it always fails, so a run that names it must fail
/// unless KNOWN_FAILURES lists it. Run only by name; --all leaves it out. UW2 only, so a
/// run of it boots one game.
/// </summary>
public sealed class HarnessFails : Scenario
{
    public override string Name => "harness-fails";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "none: a deliberate failure that tests the harness";
    public override Task Setup(ScenarioContext c) => Task.CompletedTask;
    public override Task Act(ScenarioContext c) => c.Frames(1);
    public override string Assert(ScenarioContext c) => "deliberate failure";
}
