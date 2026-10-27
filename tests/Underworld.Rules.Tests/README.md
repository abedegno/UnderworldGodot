# Underworld.Rules.Tests

UnderworldGodot's game rules checked against the original programs.

- `RngTests`: the random source. `BorlandRandSource` is Turbo C++'s `rand()`, which UW.EXE and UW2.EXE use.
- `VectorTests`: every row of each table in `vectors/` run through UnderworldGodot's version of the rule, with `Rng.Source` set to `BorlandRandSource` seeded from the row's `seed`. Each table is the original EXE's own outputs for that rule (`vectors/SOURCE` says where they come from; the tables are numbers only, no game data). Columns and the original's source for each rule are in Underworld Exhumed's `uw1/vectors/README.md` and `uw2/vectors/README.md`.
- `vectors/KNOWN_FAILURES`: tables UnderworldGodot does not match yet. A listed table still runs and prints its failing rows (`dotnet test --logger "console;verbosity=detailed"`) but passes; one that starts matching fails until its line is removed.

To refresh the tables: `./sync-vectors.sh ~/underworld-exhumed`.

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
