# Enhancements

The port aims to play as the original DOS games do. An enhancement is a deliberate change from that, and each one has its own switch in the `enhancements` section of the settings file (`user://settings.json`, class `uwsettings.Enhancements` in `src/utility/config.cs`):

```json
"enhancements": {
    "smoothcamera": true
}
```

A settings file written before a switch existed loads with that switch at its default.

## The rule for defaults

- A change to the game's rules (anything that can change what happens in play: movement, combat, timing, what the player can do) defaults to **off**, so the game plays as the original unless the player asks otherwise.
- A change to presentation only (how the same game state is drawn or heard) may default to **on**.

Turned off, an enhancement must give exactly the port's behaviour without it. A scenario in `tests/godot/scenarios` should show both that and, for a presentation switch, that the game state is the same with it on and off.

## Switches

| Switch | What it does | Default | Why |
|---|---|---|---|
| `smoothcamera` | Draws the camera every frame between its last two places from the motion update, instead of only at the update. Teleports, level changes, loads, step moves, cameras that are not the player's (do_trap_camera, roaming sight, the moongate) and any jump of more than a tile are drawn at once. | on | The motion update runs about ten times a second (`main._PhysicsProcess`), so without it the view moves in steps whatever the display's frame rate. Only the drawing changes; the game values the camera sets are worked out at the update as before (scenario `smooth-camera`). The view is drawn up to one update (about 0.1 s) behind the game. |
