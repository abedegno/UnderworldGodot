# Underworld.Rules.Tests

UnderworldGodot's game rules checked against the original programs.

- `RngTests`: the random source. `BorlandRandSource` is Turbo C++'s `rand()`, which UW.EXE and UW2.EXE use.
- `VectorTests`: every row of each table in `vectors/` run through UnderworldGodot's version of the rule, with `Rng.Source` set to `BorlandRandSource` seeded from the row's `seed`. Each table is the original EXE's own outputs for that rule (`vectors/SOURCE` says where they come from; the tables are numbers only, no game data). Columns and the original's source for each rule are in Underworld Exhumed's `uw1/vectors/README.md` and `uw2/vectors/README.md`.
- `vectors/KNOWN_FAILURES`: tables UnderworldGodot does not match yet. A listed table still runs and prints its failing rows (`dotnet test --logger "console;verbosity=detailed"`) but passes; one that starts matching fails until its line is removed.

To refresh the tables: `./sync-vectors.sh ~/underworld-exhumed`.

## Rule pins

`RulePinTests` compares the port's rule values with `pins/rule-pins.txt` and fails when any of them changed. It is there to catch a change nobody meant: a slip, or a wrong conflict resolution when the fix branches are merged. It does not say whether a value is right; the vector tests do that, against the original. The idea of a pin file comes from Underworld Revisited (PQMarine, MIT), which keeps one at `Tools/UWSelfCheck/Pins/rule-constants.txt`; the code here is our own.

The file has two kinds of line, one pin per line, sorted, as `key = value`.

`value` lines are the game assembly's named values, found by reflection. A field is pinned when its type is declared in a file under `src/`, outside `src/ui`, `src/audio`, `src/cuts`, `src/loaders` and `src/savegame` (layout, sound, cutscene playback, file readers and caches, the save format), and is not a drawing type (`...Render`) or a settings type (`uwsettings`, `JsonReadOutcome`). Nothing outside `src/` is pinned: `main.cs`, the scenario code and Godot's generated files are engine set-up. The field must be const, static readonly, or a plain static array whose start-up contents are not all zero, and its type a number, bool, string or enum, or an array of those. Many of the original's tables are plain static arrays, which is why those count; an all-zero array is working memory, and a plain static number is game state. Arrays of up to 32 elements are written out, longer ones as their length and a hash. Which folder a type is in comes from the game's PDB, so moving a file moves its pins with it.

`rule` lines are outputs of rule functions: each function in `RuleOutputPins.Cases` is called over a fixed grid of inputs, for each game it belongs to. Every row starts from the same state: `UWClass._RES` for the game, `Rng.Source` a `BorlandRandSource` seeded with the row number, and fixed byte patterns standing in for PLAYER.DAT, COMOBJ.DAT, OBJECTS.DAT and any object argument (an object gets the row's item id). The patterns are synthetic, not game data. A row's output is the return value, any ref or out argument, any PLAYER.DAT or object byte the call changed, and the random seed if the call drew. For each function the file holds the row count, a hash of all rows, sixteen short hashes of runs of rows, and four sample rows with different outputs. When a function changes, the failure names it, gives the inputs whose run changed and shows what those rows give now. On a failure every row of the run is also written to `rule-rows.txt` in the build output; running the test on the previous commit with `PINS_ROWS=1` writes the same file there, and diffing the two shows every changed row. Many of the port's formulas are literals inside the code rather than named constants, so the rule lines are what catch most changes.

Before a rule function or a static constructor runs, `GodotReach` reads its IL, and the IL of everything it calls in the game assembly, and refuses it if it reaches Godot. With no engine running such a call crashes the test host instead of throwing. A case whose every row throws, or whose every row gives the same output, fails the test too, because it pins nothing.

The test prints how many values each folder gives (`dotnet test --logger "console;verbosity=detailed"`). On 2026-10-27 that was 279 values, 233 constants and 46 tables (World 110, conversation 51, objectdata 25, npc 21, physics 17, utility 14, interaction 10, player 9, traps 9, objects 7, base 3, magic 3), and 90 rule functions, 165 (function, game) pairs with 230,991 rows between them, in 741 lines.

### What the pins do not cover

Rule functions that cannot run without Godot or game data are not pinned. Scenarios in `tests/godot` cover some of them in play.

- Every static of `npc`, because `npc`'s static constructor loads a Godot resource: `CheckIfAtOrNearTargetTile`, `set_gridx_and_y_based_on_tile_type`, `RandomLootQuality`, `GetCritterRange_seg007_17A2_30D1`, `CheckIfMatchingRaceUW2`, the `ANIMATION_*` and `COMBAT_HITFRAME` properties, and the table `NPCAnimationHeadingTable`.
- The door checks (`door.isOpen`, `isMoving`, `isSecretDoor`, `isPortcullis`, `NoOfFrames`), which reach `GRLoader` and Godot textures.
- `chargen.RollSkill`, `chargen.RollClassBaseSkills` and `GameStrings.GetDirectionStringToTile`, whose classes read game files on start-up; `chargen.InitClassAttributes`, which redraws the stats panel.
- Functions that report through the message scroll or move things in the 3D scene: the attack chain in `combat` (`AttackTarget`, `CalcAttackResults`, `checkAttackHit`, `AttackerAppliesFinalDamage`), `damage.DamagePlayer`, the hit point, mana, experience and level functions of `playerdat`, `leech.LeechDamage`, player motion, and the traps' `Activate` methods.
- Functions that need a loaded level (`UWTileMap.current_tilemap`): pathing tests between tiles, timers, NPC goals, range checks, and the barter appraisals (`ConversationVM.AssessValue`, `AppraiseOffer`).
- Functions whose output does not vary from a fixed PLAYER.DAT pattern: `playerdat.ApplyDamageResistance` (writes only statics), `SpellCasting.PlayerActiveStatusEffectSpells` (the pattern leaves no free effect slot), `an_inventory_trap.IsObjectValidForItsSlot` (no paperdoll slot holds the object), `ConversationVM.DoesNpcLike` (no conversation loaded).

Values in the left-out folders are not pinned either, nor values computed at run time from game files.

### Re-recording

When a change to a pinned value or rule is deliberate, re-record and commit the pin file in the same commit as the change, so the commit's diff shows exactly which pins it moved:

    PINS_RECORD=1 dotnet test tests/Underworld.Rules.Tests --filter RulePinTests

To pin a new rule function, add a line to `RuleOutputPins.Cases` with an axis of input values for each parameter, then re-record.

## Rules index

`RuleIndexTests` lists every routine of UW.EXE and UW2.EXE that the port implements, and fails when one routine has two implementations that nobody has said are meant. Two branches that write the same routine in two places drift apart, and nothing else catches it. The idea comes from Underworld Revisited (PQMarine, MIT), whose `Tools/UWRuleIndex` does the same from its comments.

The scanner reads the C# in `src/` and `main.cs` by path from the repository root (the tests skip when that tree is not there). A declaration counts as implementing a routine when:

- its name carries the routine's address in hankmorgan's disassembly, such as `SquareRoot_seg019_A78` or `PathAlongStraightLine_seg006_1413_205B`, or a global's, such as `BitFieldForPathing_dseg_67d6_B4`;
- its comment cites the line where Underworld Exhumed defines the routine, such as `crit_avoid_player (uw1 AI.C:685-713, uw2 AI.C:652-686)`. The citation counts when it is in the comment's first sentence, on a line of its own, right after the routine's name or under it as a label, or anywhere when the comment or the method has the routine's name. A citation of a line inside a routine, or of a routine mentioned further into the prose, does not count, because it is usually a call or a caller;
- its comment gives the routine's address alone on a line, or after a word such as "implements" or "matching".

The comment read is the one above the declaration, the one on its line and the one on its opening brace. Comments inside a method body are not read, so a routine done inline in another method is not seen.

Routines are keyed per game as `uwN:name@FILE`, the original name and the matched source file. `ruleindex/crosswalk-uw1.tsv` and `crosswalk-uw2.tsv` give, for each routine, its disassembly label and address, its name and the line that defines it. They are Underworld Exhumed's `map/crosswalk.tsv` (names and numbers only), copied by `./sync-crosswalk.sh ~/underworld-exhumed`, and `ruleindex/SOURCE` records the commit. UW1 and UW2 put different routines at the same segment and offset, so an address is looked up per game. The paragraph in an address says which game it is: UW2's segment 6 is `seg006_1413` and UW1's is `seg006_1477`, and no paragraph is used by both. Where the port leaves the paragraph out (`seg032_683`) the address is looked up in both games, and so far each one is found in only one. A data address takes its game from the data segment: `dseg_5c99` is UW1's and `dseg_67d6` is UW2's.

`ruleindex/EXPECTED_DUPLICATES` lists the routines that are meant to have more than one implementation, one line each: `routine | path#Member path#Member | reason`. The reason starts with `UW1 and UW2 variant`, `wrapper`, `deliberate split` or `duplicate, to review`. A duplicate that is not listed fails `RuleIndex_NoRoutineImplementedTwice`, and its message prints the line to add. A listed line that is no longer true fails `RuleIndex_ExpectedDuplicatesAreCurrent`. A real duplicate that has not been merged yet goes in as `duplicate, to review`, the way `vectors/KNOWN_FAILURES` works, and its line is removed in the piece that merges it.

`RULES_INDEX_RECORD=1 dotnet test --filter RuleIndex` writes `ruleindex/RULES_INDEX.md`, every routine and the file and line of each declaration that implements it. It is a listing to read and is not checked, because its line numbers move with every change.
