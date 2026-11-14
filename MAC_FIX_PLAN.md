# Bunker Buster Mac: plan for fixing "exit code 137"

## What the friend's log tells us
- Mac: Apple Silicon (arm64), macOS 27.0.1.
- The launcher worked: it removed the download block, the signature check passed, and it started the game.
- The game was killed with **exit code 137**, which means macOS force-killed it (SIGKILL) right at startup, before it printed anything.
  That is not a game crash: a game bug gives 134 or 139. An instant kill of a program whose signature checks out is almost always macOS's code-signing enforcement refusing to run it.
- The security prompt he had to override for the `.command` is expected. Only notarization removes it, and that needs a paid Apple Developer account.

## What we know about the build
- Godot's built-in "ad-hoc" signing (Godot 4.7.2 source, `CodeSign::codesign(false, true, ...)`) signs the game with **hardened runtime** switched on. For C# it also adds the allow-JIT, unsigned-executable-memory, dyld-environment and disable-library-validation entitlements. The g5 binary confirms this (signature flags = ad-hoc + runtime).
- The signature hashes all match (checked on Linux), so the files aren't damaged.

## Suspects, most likely first
1. **macOS 27 won't run an ad-hoc signature with hardened runtime and those entitlements.** Fix: sign without hardened runtime (`codesign --force --deep -s -` on the Mac, which the launcher already does when the check fails, but here the check passed), or export unsigned and sign on the Mac.
2. **.NET's JIT on Apple Silicon** trips the kernel's protection for executable memory. Test with `DOTNET_EnableWriteXorExecute=0`.
3. **A newer Gatekeeper check at launch** that kills unnotarized code even after the download block is removed. The system log or crash report will say so.
4. Fallback: **run the Intel version under Rosetta** (`arch -x86_64`), if he has Rosetta installed.

## Step 1: one test file for the friend (first job tomorrow)
Build `Mac test.command`, which goes in his existing Bunker Buster folder. One double-click, no typing, and it writes `mac-test-report.txt` for him to send back. It:
- saves the newest Bunker Buster crash report from `~/Library/Logs/DiagnosticReports/` (the "Termination Reason" line names the cause, such as CODESIGNING);
- saves the last few minutes of the kernel, code-signing and Gatekeeper log lines that mention the game;
- records the signature details (`codesign -dv`, its entitlements, `spctl --assess`);
- tries these, each on a copy of the app in a temporary folder, closing the game after a few seconds if it starts, and notes each exit code:
  - A: re-signed without hardened runtime;
  - B: as A, plus `DOTNET_EnableWriteXorExecute=0`;
  - C: the original, under Rosetta;
- tells him to just quit the game if a window opens.

## Step 2: fix the real build based on the report
- If A works: switch the `macOS` export preset off Godot's ad-hoc signing (or keep it and always re-sign without hardened runtime in the launcher on first run). Re-export, repackage with `package-mac.bat`, and verify the zip on Linux as before.
- If only B works: set that variable in the launcher.
- If only C works: have the launcher use Rosetta on Apple Silicon for now, and dig into the arm64 cause separately.
- If nothing works: the report's crash/termination reason decides the next step.

## Step 3: make the launcher report future failures by itself
Whatever the fix, the launcher should, when the game exits with an error:
- add the crash report and the relevant system-log lines to `bunker-buster-log.txt`, so nobody has to type Terminal commands;
- if it was a kill (137), try the next fallback once automatically and remember which mode worked.

Then ship it as `BunkerBuster-g5-mac-portable-r2.zip` with a short message, and update the README and `NEXT_SESSION.md`.

## Longer term
Notarization is the only way to remove the security prompts entirely and avoid this kind of signing trouble. It needs a paid Apple Developer account and a Mac, or a cloud Mac, to submit builds. Worth weighing if the Mac audience grows.
