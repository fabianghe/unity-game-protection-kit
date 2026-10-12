# Protecting a Unity game: the layered guide

You can't stop a determined attacker from reverse engineering code that runs on their own machine. The realistic goals are:

1. **Be the expensive target.** Most thieves grab whatever is easiest. Make yours slow and annoying.
2. **Ship nothing you don't have to.** Content that isn't in the build can't be stolen from it.
3. **Detect fast.** Know a copy exists before it builds an audience.
4. **Have evidence ready.** Takedowns go faster when your proof is one folder away.

The kit covers parts of 2, 3 and 4. This guide covers the rest.

---

## Layer 0: before the trailer goes viral

The most common theft of 2026 never touches your build. A clip goes viral, and within days the store fills with lookalikes made from the clip alone, often with AI. Needle In A Haystack Simulator drew at least eight Steam copycats within days of a post seen 50 million times, some of them listed before the original had a release date. The developer of the jam game 1,000,000 Potatoes accuses a studio of recreating it with AI (the studio denies it). Another developer had their game rebuilt from Twitter posts and received a demand for $200 not to publish the clone on Steam. No encryption, obfuscation or canary helps with any of this, so this layer is about being first and being provable:

- **Store page before clip.** Publish the Steam page (name, capsule, screenshots) before you post anything that could go viral. The page is a dated public record, and it's what players search for when the copies appear.
- **Trademark the name early**, including a Chinese-character version if you expect clones there. Copyright doesn't protect names.
- **Keep provable priority.** Dated devlogs, the build ledger from this kit, and an external timestamp (commit the ledger to a public repository, or use a free timestamping service) turn "I was first" into evidence a platform accepts.
- **Design depth over gimmick.** A clip-sized mechanic is copied in a day; the systems, balance and story under it are not, and players say so on the clone's page.
- **Don't pay, don't argue publicly.** Extortion threats and clone uploads both go to the platform with your evidence attached (see the playbook). Valve removes stolen listings on complaint, not proactively, so the complaint has to be yours.

---

## Layer 1: the build

### Use IL2CPP for every release build, demos included
A Mono build ships your scripts as .NET assemblies that decompile back to readable C# in seconds. Tools can then rebuild a working Unity project from the build, and that reconstructed project is exactly what gets handed to an AI to "finish the game". IL2CPP compiles to native code instead. The validator blocks Mono release builds once you've run Setup.

**What changed in 2026.** AI coding agents now decompile native binaries. In October 2026 hundreds of browser ports of console-era games appeared within weeks, reconstructed overnight by agents, and tools that put a disassembler in front of an agent are public and free. IL2CPP went from "months of expert work" to "hours of compute". It is still worth doing, because what makes recovered code usable is names and strings, and an obfuscated IL2CPP build gives the agent neither. But design as if your code will be read: nothing secret in the client, and the content locked (Layer 3).

### Obfuscate before IL2CPP
IL2CPP still keeps class, method and field names in `global-metadata.dat`, and public dumping tools turn that back into readable stubs. Names like `PuzzleBoard.SnapPiece` are what make stolen code easy to understand and extend, for humans and LLMs alike. Renaming symbols removes that.

Options (check current compatibility with your Unity version):
- **[BitMono](https://github.com/bitmono-project/BitMono)**: free, open source. Recent releases support Unity 6 and can encrypt `global-metadata.dat`.
- **[Obfuscator Pro](https://assetstore.unity.com/packages/tools/utilities/obfuscator-pro-plug-and-play-code-protection-v2026-89589)** (GuardingPearSoftware): paid, mature. Renaming plus string encryption.
- **[Mfuscator](https://assetstore.unity.com/packages/tools/utilities/mfuscator-il2cpp-encryption-256631)**: paid, focused on IL2CPP metadata encryption so automatic dumpers fail. Pair it with a renamer.
- **[LLObf](https://assetstore.unity.com/packages/tools/utilities/llobf-code-obfuscation-encryption-il2cpp-mono-373854)** (Lurid Lane): paid, first released May 2026. Claims renaming, string encryption, control flow, IL2CPP metadata protection and an optional Windows anti-cheat. No public documentation or independent review yet, and the author also publishes Unity reverse-engineering tools. Try it on a throwaway build first.

The kit integrates BitMono: install it, re-validate in Project Settings → Game Protection, and click through the fixes. The kit also scans your project for Animation Events and Inspector-wired UnityEvents and writes them into BitMono's exclusion list, because those are the names no obfuscator can see from the code. Two limits apply to every tool here. Metadata encryption stops dumpers that parse `global-metadata.dat` on disk; the game decrypts it in memory, so a memory dump still works. And BitMono's Unity integration renames `Assembly-CSharp` only: your own asmdefs and this kit's `GameProtection.Runtime` keep their names (the validator lists them) unless you run `BitMono.CLI` on those DLLs in CI.

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

## Layer 3: assets (Content Lock)

Most cloners never reverse your code. They rip your assets, watch the gameplay, and have an AI re-implement the mechanics. So the assets are what to lock.

The kit's **Content Lock** encrypts your bundles at build time and decrypts them while loading:

- **Local mode** keeps a masked key in the build. One-click rippers get nothing usable, and a repackaged copy that changes your product or company name gets an unusable key. Anyone who reads this repository can still extract it.
- **Remote mode** keeps the key on your own server. A copy gets the key only after proving entitlement with a Steam auth ticket or a Play Integrity verdict, so a re-uploaded build can't fetch it. Keys are cached device-bound, so the game works offline after the first launch, and a failed refresh never locks a paying player out.

What it costs: a few percent of CPU during bundle loading (AES on the fly), a small server (free tier) for Remote mode, and the discipline to keep demo content on the stock provider.

What it doesn't do: stop screen capture, or stop someone who buys the game and reads the key out of memory. Free runtime inspectors for Windows Unity games walk every loaded object, and one ships an MCP server so an AI agent can do it without writing code; a leaked key opens every bundle for every clone. Remote mode still removes the one-click path (a clone has no key until a buyer leaks one), ties every grant to a Steam ID in your server log, and lets you rotate keys with each content update. Treat the key as the thing a determined thief will go for.

Encrypt everything that isn't needed before the first scene, and keep your scripts obfuscated separately: Content Lock protects data, not code.

---

## Layer 4: detection

That's this kit: runtime checks catch as-is re-uploads, and the ownership canary plus the scanner prove where a rebuilt clone came from. A few more ideas:
- **Use per-recipient builds** for press, key mailers, Discord testers and festival builds (`BUILD_CHANNEL`). A leak then names its source.
- **Watch for your work being reused.** Set up search alerts for your game's name and a unique in-game phrase. Every few months, reverse-image-search your capsule art and screenshots.
- **Read your Inconclusive reports.** Installer and host details show where copies are spreading.
- **Fingerprint your art.** An AI reimplementation rewrites the code but keeps the ripped textures and audio. Perceptual hashes of your sprites and textures, compared against an extracted clone, are the evidence that survives that. The scanner doesn't do this yet; it is the next planned addition and a welcome contribution.

---

## Layer 5: China and other places where takedowns don't work

The fastest-growing theft channel is mini-games inside Douyin (China's TikTok) and WeChat: no download, no store review, free and ad-supported, with a built-in audience of hundreds of millions. Clones of Western Steam games have reached 600,000+ players there. Enforcement is weak for foreigners: full developer accounts need a Chinese national ID, complaints go through email with a one-week response window, and litigation means local counsel and notarized evidence.

The lesson is that in these markets **prevention beats enforcement**:

- **Content Lock in Remote mode.** A clone built from your ripped assets needs those assets decrypted. If they never leave your server unencrypted except to genuine copies, the clone pipeline has nothing to work with.
- **Design-level identity.** A mechanic or character that players recognise as yours makes copies visibly fake. One developer's "pirate customer" character did exactly that; the cloners didn't notice until players complained.
- **Register your copyright in China** with the CPCC through an agent (a few hundred RMB per work, one to two months; foreign companies can register in their own name). Certificates are what Chinese platforms' complaint portals accept on the first pass.
- **File your title as a trademark in China, including a Chinese-character version.** Copyright doesn't protect names, and squatters know it.

## Layer 6: your accounts and your machine

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
