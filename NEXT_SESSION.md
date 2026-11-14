# Bunker Buster (Godot): next session

Start here. Current version: **g5**. Browser reference game: DHFM91/bunker-buster (v19).

## Where things are
- Repo: DHFM91/bunker-buster-godot (not linked anywhere public until it's ready to share).
- David's PC (bigwater): `Desktop\GPT's attempt at FPS prototype\Bunker Buster Godot\` (working copy).
- Godot 4.7.2 .NET: `Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\`. .NET 8 SDK installed. Export templates installed in `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`. (The Steam copy of Godot can't do C#.)

## Workflow (no terminal on the PC; Explorer is click-only)
1. Edit in the cloud copy, commit, push.
2. Copy changed files to the PC with device_commit_files. Stage each push in a NEW folder name (e.g. outputs/pushN/): re-committing an old staged path can send a stale copy.
3. Double-click `tools\check.bat` in Explorer: builds the C#, imports, runs the self-tests (map plan + level stamping vs the browser game, seed for seed) and the automated play-test (`-- --smoketest`). Read `logs\check.txt` with device_stage_files.
4. `Play Bunker Buster.bat` runs the game from source. `tools\export.bat` makes `builds\BunkerBuster-windows.zip` (exe + .NET runtime folder). Send David the zip with a version in the name.
5. Moving the remote mouse over the captured game window spins the camera: test gameplay with the smoke test, use screenshots for looks.

## Mac build
- `tools\export-mac.bat` exports, then `tools\package-mac.bat` makes `builds\BunkerBuster-mac-portable.zip` (launcher `tools/mac/Play Bunker Buster.command` + readme + `game/Bunker Buster.app`). Send it with the version in the name, e.g. `BunkerBuster-g5-mac-portable.zip`.
- Nobody here has a Mac; a friend of David's tests. g5 as a bare `.app` gave him "The application can't be opened" even after Open Anyway; the portable folder (his suggestion) is the fix being tried. Godot's own signature on the export verifies (page hashes, sealed resources, entitlements), so suspects were quarantine/translocation, the execute bit, or the earlier Linux re-zip.
- The launcher's log (`bunker-buster-log.txt` in the game folder) has macOS version, CPU, signature check and the game's own output: ask for it whenever the Mac build fails.

## Status
- Done: level generator (identical to browser), voxel chunks with lighting, movement, shooting, breach charges, grenades, fuel drums, structural collapse, full guard AI, scoring, campaign themes, palettes, night vision, HUD, sound, Windows export.
- Leaderboard: private bin b83713eff12d (created with David's account key, which is not stored anywhere). The bin's own Security-key is only in `leaderboard.key` on bigwater (gitignored; export preset includes it). If that file is lost, create a new private bin and update Leaderboard.Url. Also look at the worst-1% frame dip from g4's textures (shader compile hitches?).
- Not yet: leaderboard (LeaderboardClient with HttpRequest; bin `json.extendsclass.com/bin/26db85ce0e56`, cut HTML after the JSON), initials entry, results-screen polish, bullet-hole/scorch decals, wall detail (stains, pipes, hazard stripes), guard corpse poses, touch/Android, settings menu (mouse sensitivity, volume, fullscreen).
- Browser NEXT_SESSION priorities still apply (smarter soldiers, more map features) once the port reaches parity.
