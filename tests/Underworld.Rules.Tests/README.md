# Underworld.Rules.Tests

UnderworldGodot's game rules checked against the original programs.

- `RngTests`: the random source. `BorlandRandSource` is Turbo C++'s `rand()`, which UW.EXE and UW2.EXE use.
- `VectorTests`: every row of each table in `vectors/` run through UnderworldGodot's version of the rule, with `Rng.Source` set to `BorlandRandSource` seeded from the row's `seed`. Each table is the original EXE's own outputs for that rule (`vectors/SOURCE` says where they come from; the tables are numbers only, no game data). Columns and the original's source for each rule are in Underworld Exhumed's `uw1/vectors/README.md` and `uw2/vectors/README.md`.
- `vectors/KNOWN_FAILURES`: tables UnderworldGodot does not match yet. A listed table still runs and prints its failing rows (`dotnet test --logger "console;verbosity=detailed"`) but passes; one that starts matching fails until its line is removed.

To refresh the tables: `./sync-vectors.sh ~/underworld-exhumed`.
