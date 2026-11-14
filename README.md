# Bunker Buster (Godot 4 + C#)

The desktop version of [Bunker Buster](https://github.com/DHFM91/bunker-buster), rebuilt in Godot 4 with C#.
The browser game stays the reference: map generation and level layout are checked against it seed for seed.

Current version: **g5** (Godot builds are numbered g1, g2, … so they don't clash with the browser game's v-numbers).

## What you need
- **Godot 4 .NET edition** (the Steam copy can't run C#): https://godotengine.org/download/windows/ → "Godot Engine – .NET". Unzip it into `C:\Godot\`.
- **.NET 8 SDK**: https://dotnet.microsoft.com/en-us/download/dotnet/8.0 → SDK → Windows x64 installer.

## Run it
- Double-click **Play Bunker Buster.bat** (builds the C# and starts the game), or
- open Godot (.NET), click **Import**, pick `project.godot`, press **F5**.

## Make a shareable build
Double-click `tools\export.bat`. Result: `builds\BunkerBuster-windows.zip` (unzip, run `BunkerBuster.exe`; no install needed).

For Macs, double-click `tools\export-mac.bat` (run `export.bat` once first so the export templates are installed). It exports one `Bunker Buster.app` for Intel and Apple Silicon (`builds\BunkerBuster-mac.zip`), then `tools\package-mac.bat` wraps it into the download for players, **`builds\BunkerBuster-mac-portable.zip`**:

```
Bunker Buster/
  Play Bunker Buster.command   launcher (tools/mac/)
  Mac - read me first.txt      player instructions (tools/mac/)
  game/Bunker Buster.app
```

The app is ad-hoc signed but not notarized. Players approve the launcher once (System Settings > Privacy & Security > Open Anyway); it then clears the download quarantine from the folder, makes sure the game is executable, re-signs it on the Mac if the signature doesn't verify there, starts the game and writes `bunker-buster-log.txt` next to itself. The packer writes Unix permissions into the zip (755 for the launcher and the executables) so they survive unzipping on a Mac. Double-click `tools\package-mac.bat` on its own to repackage an existing Mac export.

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
| Space | jump |
| Mouse wheel | fire mode: single / 3-round burst / auto |
| C | crouch |
| Esc | pause (E in the pause menu ends the run and submits the score) |
| E | on the level-clear screen: end the run and submit the score |
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
- **g5:** end a run whenever you like: E on the level-clear screen, or Esc then E mid-level (the level in progress isn't scored); dying ends it too. Run-over screen with the global top 10 and arcade initials entry. The board lives in a private extendsclass bin: its key is read from `leaderboard.key` (local only, packed into exports by the export preset, never committed); without that file the board shows as unavailable.
- **g4:** fire modes on the mouse wheel (single, 3-round burst, automatic, with spread that builds in sustained fire); jumping (about 2 blocks: hop sandbags, rubble and crates, stand on things, drop off them); detailed 256 px PBR wall and prop textures generated at start-up (albedo, normal and roughness maps) with anisotropic filtering, per-block shade variation and back-face culling. Benchmark (`tools\benchmark.bat`): average fps unchanged (~310 on an RX 9060 XT), worst-1% frames ~100-110 vs ~170 before.
- **g3:** detailed 3D soldiers (helmet, goggles, plate carrier, pouches, radio, rifle; four camouflage schemes and skin tones) with procedural animation: walk cycle, idle breathing and head scanning, low/ready carry, shouldered aim, recoil and muzzle flash, hit flinch and three death falls. HUD rebuilt like the browser version (objective compass arrow and distance, alert bar, vitals, carbine / breach kit with GRN (PRESS G), Q PLANT CHARGE prompt, charge meter). The click that starts a level no longer fires a round, so the first sentry doesn't come running.
- **g2:** the game proper: shooting (each round knocks out a block of cracked concrete), breach charges, grenades, exploding fuel drums, structural collapse with falling debris and rubble piles, the full guard AI (four alert phases, hearing, sight, the one-second report window, reinforcements, sentries), dodgeable enemy rounds, health, the carbine viewmodel, sound, itemised scoring with the level multiplier, and runs that end when you die.
- **g1:** first playable Godot build: random base from the browser game's generator, real 3D lighting (sun and shadows, sky, fog, wall lamps, ambient occlusion), walk and crouch, grab the intel and get back to the pad, endless levels with the seven themes. Guards stand at their posts but don't act yet; no shooting yet.
