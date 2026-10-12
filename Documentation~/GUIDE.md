# Protecting a Unity game: the layered guide

You can't stop a determined attacker from reverse engineering code that runs on their own machine. The realistic goals are:

1. **Be the expensive target.** Most thieves grab whatever is easiest. Make yours slow and annoying.
2. **Ship nothing you don't have to.** Content that isn't in the build can't be stolen from it.
3. **Detect fast.** Know a copy exists before it builds an audience.
4. **Have evidence ready.** Takedowns go faster when your proof is one folder away.

The kit covers parts of 2, 3 and 4. This guide covers the rest.

---

## Layer 1: the build

### Use IL2CPP for every release build, demos included
A Mono build ships your scripts as .NET assemblies that decompile back to readable C# in seconds. Tools can then rebuild a working Unity project from the build, and that reconstructed project is exactly what gets handed to an AI to "finish the game". IL2CPP compiles to native code instead. The validator blocks Mono release builds once you've run Setup.

### Obfuscate before IL2CPP
IL2CPP still keeps class, method and field names in `global-metadata.dat`, and public dumping tools turn that back into readable stubs. Names like `PuzzleBoard.SnapPiece` are what make stolen code easy to understand and extend, for humans and LLMs alike. Renaming symbols removes that.

Options (check current compatibility with your Unity version):
- **[BitMono](https://github.com/bitmono-project/BitMono)**: free, open source. Recent releases support Unity 6 and can encrypt `global-metadata.dat`.
- **[Obfuscator Pro](https://assetstore.unity.com/packages/tools/utilities/obfuscator-pro-plug-and-play-code-protection-v2026-89589)** (GuardingPearSoftware): paid, mature. Renaming plus string encryption.
- **[Mfuscator](https://assetstore.unity.com/packages/tools/utilities/mfuscator-il2cpp-encryption-256631)**: paid, focused on IL2CPP metadata encryption so automatic dumpers fail. Pair it with a renamer.

**Obfuscation gotchas.** Exclude anything that is called by name:
- `SendMessage` and `Invoke`
- Animation Events
- UnityEvents wired in the Inspector
- reflection
- serialized field names (`JsonUtility`, Newtonsoft)
- SDK callbacks

Then play the obfuscated IL2CPP build start to finish before every release. The editor proves nothing here.

### Don't ship the "DoNotShip" folders
IL2CPP writes a `*_BackUpThisFolder_ButDontShipItWithYourProduct` folder next to standalone builds, containing generated C++ and symbols. Uploading it to Steam is a common mistake. The kit moves it out automatically.

### Keep Unity patched
Unity runtime vulnerabilities happen. In October 2025 Unity published [CVE-2025-59489](https://unity.com/security/sept-2025-01), which affects games built with 2017.1 and later on Android, Windows, macOS and Linux. Rebuilding with a patched editor is the recommended fix. Rebuild older shipped games too.

---

## Layer 2: demos

Demos are the most common starting point for theft: free, widely downloaded, and often the whole game with a flag check.

- **Build the demo separately.** In Unity 6 use a dedicated Build Profile with its own scene list and a `GAME_DEMO` define. Keep full-game Addressables groups out of demo builds.
- **Strip code, not just content.** Wrap full-game-only systems in `#if !GAME_DEMO` so the code for later features doesn't ship either.
- **Let the kit enforce it.** List your demo scenes and full-game-only folders in Release Guard Settings. A demo build that would leak them fails.
- **Sign your work inside the game.** A splash or credit screen with your name and your official link proves ownership instantly, and tells players of a stolen copy where the real game is.

---

## Layer 3: assets

Be realistic: anything rendered on screen can be captured. Encrypting AssetBundles or Addressables stops one-click extraction, not a determined ripper, and it costs load time. Use it for genuinely premium content (licensed music, high-value art packs), not for everything.

---

## Layer 4: detection

That's this kit: runtime checks catch as-is re-uploads, and the ownership canary plus the scanner prove where a rebuilt clone came from. A few more ideas:
- **Use per-recipient builds** for press, key mailers, Discord testers and festival builds (`BUILD_CHANNEL`). A leak then names its source.
- **Watch for your work being reused.** Set up search alerts for your game's name and a unique in-game phrase. Every few months, reverse-image-search your capsule art and screenshots.
- **Read your Inconclusive reports.** Installer and host details show where copies are spreading.

---

## Layer 5: your accounts and your machine

The newer threats target developers rather than builds:
- **Account takeover.** Use hardware-key or app-based 2FA and a dedicated email for Steamworks, Play Console, App Store Connect and Unity. A hijacked publisher account can push a malicious update to your real players.
- **Stolen store assets as bait.** Fake store pages built from other games' screenshots have been used to distribute malware disguised as demos. Report copies of your assets quickly.
- **"Playtest my game" and fake publisher offers** that arrive with an executable or an "NDA tool". Treat them as hostile.
- **Supply chain.** Install packages and assets only from sources you trust, and review what editor scripts run on import.

---

## What not to bother with

- **DRM wrappers and ownership checks as protection.** They are stripped quickly. Use them for telemetry, not enforcement.
- **Crashing or bricking on tamper detection.** False positives hit paying players, and players of stolen copies blame your game.
- **Heavy anti-debug tricks** in a single-player indie game. They cost you more in bug reports than they cost attackers.

If you have something to add, a pull request to this guide is very welcome.
