# sotf-rigprobe

A RedLoader research mod for Sons of the Forest that dumps character data to JSON: skeletons, skinned meshes, bind poses, blendshapes, materials, animators, player races and clothing. It is a data source for building character related mods, not a gameplay mod.

## What it collects

For the local player, remote players, the bodies the player can carry, every actor type prefab and every live actor:

- Rig: every bone with its parent, local position, rotation and scale
- Skinned meshes: mesh name, vertex count, render layer, shadow mode, bone list, bind pose rest position and rotation per bone, blendshape names, materials with shader and texture names
- Components: script types on the root, its children, rig bones and animator objects
- Animators: controller, avatar, humanoid flag, human bone map, layer names and weights, parameters with hashes, clips with length, loop flag and events, discovered states, current state per layer
- Rig families: characters grouped by identical bone sets, with bone name overlap against the player rig
- Player only: current race, all 8 race entries with head and arms Addressable GUIDs, every clothing piece with item id, slot, renderable GUID, default and worn flags

## Commands

Run these in the in-game console.

| Command | What it does |
|---|---|
| `rigprobe` | Dump the player, remote players, carry bodies, all actor prefabs and live actors, plus all loaded clips and controllers |
| `rigprobe <filter>` | Same, limited to actor types containing the filter, e.g. `rigprobe female` |
| `rigscene` | Dump every character in the loaded scenes (posers, cutscene characters, props with skinned meshes), one file per character |
| `rigwatch [player\|robby\|virginia]` | Log every animator state change on the target while you play |
| `rigwatch off` | Stop watching and write a summary of every state seen |
| `rigspawn <Type> [variation]` | Spawn an actor in front of you so its live variant gets dumped, e.g. `rigspawn Virginia` |
| `rigplay <layer> <state\|hash> [fade]` | CrossFade the local player into an animator state, fade 0 uses Play, e.g. `rigplay fullBodyActions couchIdle` |
| `rigparam <name> [value]` | Set a local player animator parameter, e.g. `rigparam couchBool 1` |
| `rigweight <layer> <weight>` | Set a local player animator layer weight |
| `rignet` | Hook the `updateMecanimRemoteState` Bolt event, its receivers and any method with RemoteState or Mecanim in its name, and log every call |

## Output

Written to `Sons Of The Forest\UserData\RigProbe\`:

- `characters\index.tsv` one line per character with rig family and player bone match
- `characters\clips.tsv` every loaded animation clip, `characters\controllers.tsv` every loaded animator controller and who uses it
- `watch\<target>_<time>.log` and `_states.tsv` from `rigwatch`. The log has a kind column: `state`, `weight` (layer weight changes), `param` (bool, int and trigger changes) and `net` (`rignet` lines while both run)
- `net\net_<time>.log` from `rignet`
- `characters\rigfamilies.txt` characters grouped by shared skeleton
- `characters\<source>_<name>.json` full data per character
- `scene\` the same layout for `rigscene`


## Addressables catalog

Walking the catalog from inside the game crashes it, so the catalog is decoded offline instead. `tools/decode_catalog.py` reads `SonsOfTheForest_Data\StreamingAssets\aa\catalog.json` and writes every key with its asset path, type and provider as TSV. Use it to resolve any AssetReference GUID to its asset.

```
python tools\decode_catalog.py "C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest\SonsOfTheForest_Data\StreamingAssets\aa\catalog.json" data\assets.tsv
```

## Build

Requires the .NET 8 SDK and RedLoader installed in the game folder with its interop assemblies generated.

```
.\build.ps1 -Install
```

## Reference data

`data/` holds a captured dump and `data/FINDINGS.md`, a summary of the player rig, races, clothing, rig families and female assets. Use it as a starting point before running the mod yourself.
