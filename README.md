# sotf-rigprobe

A RedLoader research mod for Sons of the Forest that dumps character data to JSON: skeletons, skinned meshes, bind poses, blendshapes, materials, animators, player races and clothing. It is a data source for building character related mods, not a gameplay mod.

## What it collects

For the local player, the bodies the player can carry, every actor type prefab and every live actor:

- Rig: every bone with its parent, local position, rotation and scale
- Skinned meshes: mesh name, vertex count, bone list, bind pose rest position and rotation per bone, blendshape names, materials with shader and texture names
- Animators: controller, avatar, humanoid flag, human bone map, parameters
- Rig families: characters grouped by identical bone sets, with bone name overlap against the player rig
- Player only: current race, all 8 race entries with head and arms Addressable GUIDs, every clothing piece with item id, slot, renderable GUID, default and worn flags

## Commands

Run these in the in-game console.

| Command | What it does |
|---|---|
| `rigprobe` | Dump the player, carry bodies, all actor prefabs and live actors |
| `rigprobe <filter>` | Same, limited to actor types containing the filter, e.g. `rigprobe female` |
| `rigscene` | Dump every skinned mesh root in the loaded scenes, including cutscene characters |
| `rigspawn <Type> [variation]` | Spawn an actor in front of you so its live variant gets dumped, e.g. `rigspawn Virginia` |

## Output

Written to `Sons Of The Forest\UserData\RigProbe\`:

- `characters\index.tsv` one line per character with rig family and player bone match
- `characters\rigfamilies.txt` characters grouped by shared skeleton
- `characters\<source>_<name>.json` full data per character
- `scene\` the same layout for `rigscene`

Each run replaces the previous output for that command.

## Build

Requires the .NET 8 SDK and RedLoader installed in the game folder with its interop assemblies generated.

```
.\build.ps1 -Install
```
