# RigProbe findings

Captured with RigProbe v0.3.0 on 2026-09-24.

## Player

- Animation root: `LocalPlayer/PlayerAnimator/Root`, skeleton starts at `Hips`
- Animator is Generic, not Humanoid (`PlayerAControllerGenerated`, 12 layers, 271 parameters). No character in the game uses a Humanoid avatar, so Unity retargeting is not available and body swaps need a manual bone map.
- The visible body is head + arms (race system) + clothing (clothing system). There is no base body mesh.
- Head has 2 expression blendshapes: 0 Angry, 1 Scared. `ExpressionBlendsStartIndex` is 0.
- `SkinnedMeshBoneRemapCache` (Endnight.Animation) retargets meshes onto the player rig by bone path.

## Races

`PlayerRaceSystem._races` holds 8 entries, applied by list index. Entry 6 (asset `BlackBRace`) has its race field set to `BlackA`, but it has its own head and works correctly in game.

| Index | Race | Asset | Head GUID | Arms GUID |
|---|---|---|---|---|
| 0 | White | WhiteRace | c6b5d71936b7773418409c28fbedc0a4 | 3886900e0d13c3d4f97a4795c8d0f0bb |
| 1 | Black | BlackRace | 091aa8cb2ce206441b1ff4d1a3c02ab9 | 7b273beb092763143bab57f876dce75d |
| 2 | Latin | LatinRace | c60e9927531d33745b61950677621768 | dca5d9c41251d324e9af4c916f91a049 |
| 3 | Asian | AsianRace | a90a307a8c0607c40b68d376074715df | 3ce89e7484ac88244b1df4b6ae8a4c6f |
| 4 | BlackA | BlackARace | ff992ec27e7740142a305730e65ec46a | 7b273beb092763143bab57f876dce75d |
| 5 | WhiteA | WhiteARace | c67c50b0bbf49e244bb0d2ac839d9b69 | 3886900e0d13c3d4f97a4795c8d0f0bb |
| 6 | BlackB | BlackBRace | 2a66a9d5f2b53cc4185d37f4b8a59b37 | b1e878151ef75294687db0e618ec4844 |
| 7 | LatinA | LatinARace | 65c86538006018140835657ca528e0c1 | b1cd63fc76d99f94694fe1fc73e67f9a |

## Clothing

Defaults are TacticalJacket, TacticalPants, TacticalBoots and Backpack.

| Item | Asset | Slots |
|---|---|---|
| 402 | Backpack | Back |
| 444 | TacticalRebreather | Back |
| 487 | SilkPyjamas | Torso, Legs, Feet, Overcoat |
| 489 | TacticalPants | Legs |
| 490 | Hoodie | Torso, Overcoat |
| 491 | OldJacket | Torso, Overcoat |
| 492 | Tuxedo | Torso, Legs, Feet, Overcoat |
| 493 | LeatherJacket | Torso, Overcoat |
| 495 | TacticalJacket | Torso, Overcoat |
| 499 | Wetsuit | Torso, Legs, Feet, Overcoat |
| 500 | PuffyJacket | Overcoat |
| 501 | TacticalBoots | Feet |
| 572 | GoldenArmour | Head, Torso, Legs, Hands, Feet, Overcoat |
| 639 | SpaceSuit | Torso, Legs, Feet, Overcoat |
| 703 | PriestOutfit | Torso, Legs, Feet, Overcoat |
| 749 | FlightAttendantUniform | Torso, Legs, Feet, Overcoat |

## Rig families

Bone name overlap is against the player rig.

| Family | Members | Bones | Player match |
|---|---|---|---|
| 3485f157 | Player | 205 | 100% |
| a240987b | Player cutscene rigs | 122 | 96% |
| 09b012a0 | TacticalSoldier A/B/D, BossMutantChopper | 125 | 93% |
| 02a67127 | Robby cutscene | 132 | 88% |
| 1b02206c | Robby (Kelvin) | 93 | 86% |
| db3f3ed0 | WorkerDude posers | 94 | 86% |
| c59ddba4 | GoldArmourSkeletonPosed | 60 | 75% |
| 007bf83f | TimmysDad | 141 | 70% |
| d31a5f4b | TacticoolCaucasianPoserRig | 158 | 65% |
| 18772f39 | Male cannibals, male carry body | 91 | 0% |
| e5ab1cff | Female cannibals (Angel, Brandy, Crystal, Destiny, Elise, MuddyFemale), female carry body | 67 | 0% |
| fe742020 | DeadMaleA/B/C, DeadFemaleC | 89 | 0% |
| 19b9a8ac | DeadFemaleB (bar, pool, gym) | 87 | 0% |
| fcd908ad | HeavyMale, FacelessMale | 86 | 0% |
| e53f4f23 | FatMale, FatFemale | 90 | 0% |
| f8c8bc7e | Mr/Miss Puffy, Puffton bosses, puffy carry bodies | 63 | 0% |
| f7fc845b | Virginia | 161 | 0% |

## Rig naming

Player style: `Hips`, `Spine`, `Spine1`, `Spine2`, `Neck`, `Neck1`, `Head`, `LeftShoulder`, `LeftArm`, `LeftForeArm`, `LeftHand`, `LeftHandIndex1`, `LeftUpLeg`, `LeftLeg`, `LeftFoot`, `LeftToeBase`.

`C1:` style (cannibals, dead civilians, puffies): `C1:c_hip_SC`, `C1:C_spine0_SC`, `C1:C_neck0_SC`, `C1:C_head0_SC`, `C1:L_armPrnt0_SC`, `C1:L_arm0_SC`, `C1:L_arm1_SC`, `C1:L_hand00_SC`, `C1:l_handAFingerA0_SC`, `C1:L_leg0_SC`, `C1:L_leg1_SC`, `C1:L_foot00_SC`. Civilian dead bodies add twist and helper joints (`_JNT`).

Virginia uses a separate `C1:` rig with an `A` suffix (`C1:C_spineAHip_SC`, `C1:R_armA0_SC`) and face bones.

## Female assets

- `LocalPlayer/CarryBody/FemaleCannibal` exists on every player, vanilla included. Main mesh `C1:Female_lp`: 14209 verts, 67 bones, blendshapes 0 scared and 1 angry.
- Civilian women exist only as scene posers: `DeadFemaleBodyB/C` with separate `DeadFemaleHeadB/C`, outfits for pool, gym and bar, and `RichWomanCostume`.
- Virginia: `C1:Virginia_body`, 129 bones, 20 blendshapes, outfits camosuit, dress, leather suit, tracksuit, tutu.

## Addressables catalog

Decoded from `catalog.json` into `data/assets.tsv` (5523 keys, 2917 entries). Race and clothing GUIDs resolve to prefabs, for example `65c86538006018140835657ca528e0c1` is `Assets/Sons/Characters/PlayerA/Variations/LatinA/LatinAHead.prefab` and `c84bc6d4586c5f04b9eabc8d26ad54fe` is `Assets/Sons/Wearables/Clothing/TacticalJacket.prefab`.

- Loadable from anywhere: all player race heads and arms (including extra variants like `WhiteHeadBalaclava`, `LatinArmsFull`, `AsianHeadNoHair`), every clothing piece, Kelvin's full wardrobe (`Assets/Sons/Characters/Robby/Robby*Addressable.prefab`), Timmy and TimmysDad parts, `FatFemaleCannibalSkin`, `FingersSkinAddressable`, `TwinsSkinAddressable`, `PlayerA/Poses/CultRobe*`, `playerARenderRig.fbx`, `VirginiaRig.fbx`.
- Not addressable: the dead civilian women (`DeadFemaleBody/Head`) and the female cannibal body meshes. They only exist when their scene or actor is loaded. The female carry body on the player is the reliable source.
- In-game `Locate` with a wrapped string key returns nothing and walking the main catalog crashes the game, so use the offline decode.

## Data layout

- `characters/` full `rigprobe` output: player, carry bodies, every actor prefab, live actors
- `scene/index.tsv` and `scene/rigfamilies.txt` from `rigscene`
- `scene/samples/` one JSON per scene rig family
- `assets.tsv` decoded Addressables catalog
