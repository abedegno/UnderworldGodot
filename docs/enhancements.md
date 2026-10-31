# Enhancements

The port aims to play as the original DOS games do. An enhancement is a deliberate change from that, and each one has its own switch in the `enhancements` section of the settings file (`user://settings.json`, class `uwsettings.Enhancements` in `src/utility/config.cs`):

```json
"enhancements": {
    "smoothcamera": true,
    "crittermodels": "off"
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
| `crittermodels` | Draws UW1 creatures as 3D models instead of sprites: `"model"` (a skinned mesh) or `"voxel"` (voxels), from the folder in the `modelpath` setting. A creature whose body has no model there stays a sprite, and `"off"` draws every creature as a sprite. | off | A player who has made 3D models of the creatures can use them. The game runs exactly as before: the models show the sequence and frame the game gives the sprite, and each creature type's own aux palette colours its model, as the original colours the sprites, so the four goblins share one model. Two drawing changes: a model shows an attack from every side, where the sprites have attack art only for the front (`npc.SetAnimSpriteUW1`), and it turns smoothly with the creature's fine heading (scenario `critter-models`). A creature drawn as a model is clicked, killed, saved and restored and left on another level as with sprites, and a save is the same byte for byte with the switch on or off (scenario `critter-models-life`). |
| `itemmodels` | Draws UW1 floor objects as 3D models instead of sprites: `"model"` (a mesh, or voxels where an icon has only a voxel file) or `"voxel"` (voxels), from the folder in the `modelpath` setting, one model per icon of `OBJECTS.GR` (`uw1/OBJnnn.glb` and `uw1/OBJnnn.vox`, nnn the icon in hex). An icon with no model there stays a sprite, and `"off"` draws every object as a sprite. | off | A player who has made 3D models of the objects can use them, to match the creature models. The game runs exactly as before. One drawing change: a model lies still, turned by the object's heading, where the sprite turns to face the viewer (scenario `item-models`). |

## Creature models

The files for `crittermodels` go in `<modelpath>/uw1/`, named after the critter page files a body is drawn from (the body's number in octal, as in `CR00PAGE.N00`): `CR00.glb` for `"model"` and `CR00.vox` for `"voxel"`. They are made from the game's own art, so they come with the player's game files and never with the port. `src/npc/crittermodel.cs` describes both formats: the GLB's texture holds each pixel's slot in the creature's aux palette rather than a colour, and its clips are named after the sequences (`idle`, `idle_combat`, `walking`, `attack_bash`, `attack_slash`, `attack_stab`, `attack_secondary`, `death`), one key frame per frame of the sequence at eight a second.
