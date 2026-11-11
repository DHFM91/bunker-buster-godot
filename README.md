# Bunker Buster (Godot 4 + C#)

The desktop version of [Bunker Buster](https://github.com/DHFM91/bunker-buster), rebuilt in Godot 4 with C#.
The browser game stays the reference: map generation and level layout are checked against it seed for seed.

Current version: **g2** (Godot builds are numbered g1, g2, … so they don't clash with the browser game's v-numbers).

## What you need
- **Godot 4 .NET edition** (the Steam copy can't run C#): https://godotengine.org/download/windows/ → "Godot Engine – .NET". Unzip it into `C:\Godot\`.
- **.NET 8 SDK**: https://dotnet.microsoft.com/en-us/download/dotnet/8.0 → SDK → Windows x64 installer.

## Run it
1. Open Godot (.NET), click **Import**, pick this folder's `project.godot`.
2. Press **F5** (or the ▶ button top right).

## Check it
Double-click `tools\check.bat`. It compiles the C#, imports the assets and runs the self-tests (the C# map and level
generators against the browser game's own output). The report lands in `logs\check.txt`.

## Controls
| Key | Action |
|---|---|
| WASD | move |
| Mouse | aim |
| Click (hold) | fire |
| R | reload |
| Q | plant breach charge |
| G | throw grenade |
| C | crouch |
| Esc | pause |
| F9 | new random base (testing) |

## Layout
| Path | What it is |
|---|---|
| `scripts/World/MapPlan.cs` | The level planner (maze, foyer, yards, atrium, hall, reinforced walls, gates, windows, vents, posts, props). Same random sequence as the browser game. |
| `scripts/World/LevelBuilder.cs` | Stamps the plan into voxels, places lamps, trucks, cover and sentry posts. |
| `scripts/World/VoxelWorld.cs` | The voxel grid (one byte per voxel, one Godot unit per voxel). |
| `scripts/World/ChunkMesher.cs` | Turns 16×16 chunks into meshes, with corner shading. |
| `scripts/World/WorldView.cs` | Sky, sun, fog, lamps, chunk meshes. |
| `scripts/World/Theme.cs` | The seven level themes and the time-of-day moods. |
| `scripts/World/Destruction.cs` | Voxel ray tracing, carving holes, structural collapse and falling debris. |
| `scripts/Guards/` | The garrison: alert phases, flow-field pathing, sight and hearing, the report window, reinforcements, sentries, visible rounds. |
| `scripts/Player/PlayerController.cs` | First-person movement and look. |
| `scripts/Player/PlayerCombat.cs` | Rifle, reload, breach charges, grenades, fuel drums, explosions, health. |
| `scripts/Player/ViewModel.cs` | The carbine drawn in front of the camera (same drawing as the browser game). |
| `scripts/Core/` | HUD, sound effects, particle effects and the score calculator. |
| `scripts/Tests/` | Self-tests run by `tools\check.bat`. |
| `reference/` | Node scripts that produce the browser game's reference output, plus that output. |
| `assets/` | Textures, sprites and sounds exported from the browser game (`tools/asset-export.js`). |

## Version history
- **g2:** the game proper: shooting (each round knocks out a block of cracked concrete), breach charges, grenades, exploding fuel drums, structural collapse with falling debris and rubble piles, the full guard AI (four alert phases, hearing, sight, the one-second report window, reinforcements, sentries), dodgeable enemy rounds, health, the carbine viewmodel, sound, itemised scoring with the level multiplier, and runs that end when you die.
- **g1:** first playable Godot build: random base from the browser game's generator, real 3D lighting (sun and shadows, sky, fog, wall lamps, ambient occlusion), walk and crouch, grab the intel and get back to the pad, endless levels with the seven themes. Guards stand at their posts but don't act yet; no shooting yet.
