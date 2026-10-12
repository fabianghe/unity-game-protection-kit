# Game Protection Kit for Unity

**Protect any Unity game from theft: encrypt your content so stolen copies have no game in them, detect re-uploaded and repackaged builds, and prove ownership when a clone appears.**

Indie games and demos get decompiled, "finished" with AI, and re-uploaded to Steam, mobile stores and Chinese mini-game platforms, sometimes with ads injected or malware attached. In many of those places takedowns are slow or impossible for a foreign solo developer. So this kit is built around a different idea: **don't rely on takedowns.** Ship content that only a genuine copy can decrypt, make rebuilt clones impossible to assemble with one-click tools, and keep the evidence ready for the cases where a takedown does work.

- ✅ Drop-in: install from a git URL, click **Setup**, done. No code required.
- ✅ Never punishes real players: no DRM crashes, no lockouts, offline play after the first launch.
- ✅ Private by default: sends nothing anywhere unless you plug in your own analytics or key server.
- ✅ Works without any third-party SDK. Steamworks.NET, Addressables, Firebase and Play Integrity are optional and auto-detected.

---

## What it does

### 1. Content Lock: encrypted assets, keys off the device

Your AssetBundles / Addressables bundles are encrypted at build time (AES-256-CTR) and decrypted on the fly while loading. Asset rippers (AssetRipper, AssetStudio and the like) can't open them, so a thief can't rebuild your project from a build.

Two modes:

| | Local mode | Remote mode |
|---|---|---|
| Where is the key? | Inside the build, masked | On **your** key server, never in the build |
| Stops one-click rippers | ✅ | ✅ |
| Stops someone who reads this repo | ❌ (they can extract the key) | ✅ (there is no key to extract) |
| Re-uploaded copy | Still runs | **Can't fetch keys** (no valid Steam ticket / Play Integrity verdict for a copy the player doesn't own) |
| Needs a server | No | Yes, a free Cloudflare Worker (included, ~10 minutes to deploy) |
| Needs internet | Never | First launch only; keys are cached device-bound after that |

Remote mode is the real protection against copies that never had a key: a re-upload, a demo pointed at paid content, a clone assembled from ripped files. It is not a wall against a legitimate buyer. Free runtime inspectors for Windows Unity games can read the key ring out of a running copy (one ships an MCP server, so an AI agent can do it without writing code), and a leaked key opens every bundle for every clone. What Remote mode changes is the shape of the attack: from "download and rip" to "buy, run, extract, redistribute", with the buyer's Steam ID in your server log and a key you can rotate with the next content update.

Works with **Addressables** (switch groups to the *Encrypted AssetBundle Provider*, one click) and with **plain AssetBundles** (`ContentLock.LoadBundle(path, callback)` instead of `AssetBundle.LoadFromFile`).

### 2. Detection: know when a copy exists

| Check | Catches | Platforms |
|---|---|---|
| App identity | Re-uploads with a different package name, product or company name | All |
| Android signature | Repackaged APKs: injected ads, "mods", store re-uploads | Android |
| Android installer | Where copies are installed from (reported, never fails) | Android |
| Steam App ID | Builds re-uploaded under another App ID, demos sold as full games | Desktop + Steamworks.NET |
| Web host | WebGL builds rehosted on portals you didn't approve | WebGL |
| Your own | Anything: implement `IIntegrityCheck`, one method | Any |

Results go to your analytics (optional), and a stolen copy can show its players where the official game lives.

### 3. Build guards: don't ship what shouldn't ship

- **Demo leak guard.** Demo builds fail if they contain a non-demo scene, a scene that references full-game assets, or full-game content inside `Resources`. Locked content is still shipped content.
- **IL2CPP enforcement.** Release builds fail on the Mono backend, which decompiles back to near-original C# in seconds.
- **Obfuscation, one click.** Warns when no obfuscator is detected (IL2CPP alone leaves class and method names readable) and integrates **BitMono** (free, open source): checks its config, enables `global-metadata.dat` encryption for IL2CPP, and writes **Unity-safe exclusions** (Animation Events, UnityEvents) into its config so an obfuscated build doesn't silently lose callbacks.
- **"DoNotShip" cleanup.** IL2CPP's C++/symbols backup and Burst debug folders are moved out of the build so they never reach Steam.

### 4. Evidence: prove it's yours

- **Build stamping.** Every build gets an ID and a channel (`full`, `demo`, `press-outlet`, `discord-testers`…), logged to `GameProtectionBuildLedger.csv`. A leaked copy tells you where it came from.
- **Ownership canary.** A permanent, project-unique token goes into every build's data and into a hidden object in every scene. Rippers carry it into rebuilt clones without noticing.
- **Scan Suspicious Build.** Point it at a suspected copy (extracted) and it finds your token and build IDs, then writes a dated report for your takedown or lawyer.

---

## Install

**Package Manager → + → Add package from git URL…**

```
https://github.com/fabianghe/unity-game-protection-kit.git
```

Pin a version with `#v0.3.0` at the end. Requires Unity 2021.3 or newer; built against Unity 6.

## Set up (5 minutes)

1. **Tools → Game Protection → Setup.** Creates the settings assets pre-filled from Player Settings and opens **Project Settings → Game Protection**.
2. **Copy your ownership token** (Release guards → Ownership canary) into your evidence folder. Never change it.
3. Fix anything the validator lists (most fixes are one click) and fill in what only you know: official store URL, Android cert SHA-256 (Play Console → App integrity), Steam App IDs, allowed web hosts, demo scenes and full-game-only folders.
4. For demos, add `GAME_DEMO` to your demo Build Profile's scripting defines.

That's detection and build guards. Nothing runs until Setup has been done, so installing the package in a project changes nothing by itself.

### Turn on obfuscation (BitMono)

1. Download the UPM `.tgz` for your Unity version from [BitMono's releases](https://github.com/bitmono-project/BitMono/releases) and add it with **Package Manager → + → Add package from tarball**.
2. **Project Settings → Game Protection → Re-validate**. Follow the fixes: enable obfuscation, enable metadata encryption (IL2CPP, Windows/Android), and **Add Unity-safe exclusions** (re-run it after adding new Animation Events or Inspector-wired UnityEvents).
3. Build and play through the build. Obfuscation bugs only show up in builds, never in the editor.

Note: BitMono's Unity integration obfuscates `Assembly-CSharp` only. The validator lists any of your own assembly definitions that would stay readable.

### Turn on Content Lock

1. In **Project Settings → Game Protection → Content Lock**, tick **Enabled**. A content key is created; **back up `Assets/GameProtection/Editor/ContentLockKeys.asset`** offline and keep it out of git (the validator warns if it is tracked; CI can take the keys from the `GAMEPROTECTION_CONTENT_KEYS` environment variable).
2. **Addressables:** Tools → Game Protection → Content Lock → **Use Encrypted Provider For All Groups** (or pick the provider per group in the group's schema). Keep *demo* groups on the stock provider. Rebuild Addressables content; the bundles are encrypted automatically after every content build.
   **Plain AssetBundles:** Tools → Game Protection → Content Lock → **Encrypt Bundles In Folder…**, and load them with `ContentLock.LoadBundle`.
3. Start with **Local** mode and play through a build. Everything should work exactly as before.
4. For **Remote** mode: deploy the key server (`Server~/cloudflare-worker/README.md`, 10 minutes), paste the URL into the config, and you're done on desktop if Steamworks.NET is installed (the Steam proof registers itself). On Android import the **Play Integrity Proof** sample.

> Rule that keeps Remote mode safe: the key server allows **paid App IDs only**, demo builds never carry keys (enforced), and demo groups stay unencrypted. Otherwise a thief could point a build at your free demo and get the keys.

---

## Customize (optional)

```csharp
using GameProtection;
using GameProtection.Content;

GameProtectionKit.Reporter = new MyAnalyticsReporter();   // where integrity reports go (Firebase sample included)
GameProtectionKit.NoticePresenter = new MyNoticeUi();      // what players of a stolen copy see
GameProtectionKit.RegisterCheck(new MyCustomCheck());      // add your own checks
GameProtectionKit.RunNow();                                // run checks on demand (e.g. right after Steam init)
IntegrityEvents.ReportReady += report => Debug.Log(report);

ContentLock.ProofProvider = new MyEntitlementProof();      // your own platform proof (Epic, GOG, consoles...)
ContentLock.KeyProvider = new MyKeyProvider();             // replace the whole key strategy
ContentLock.RefreshKeys(result => { });                    // after a purchase or key rotation
ContentLock.LoadBundle("path-or-url", (bundle, error) => { });
```

Set these from your own bootstrap (`Awake`, or `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`).

**Samples** (Package Manager → Game Protection Kit → Samples): Firebase Reporter, UnityEvent Notice (no-code UI), Custom Check template, Play Integrity Proof (Android, Remote mode).

### Per-recipient builds

Set `BUILD_CHANNEL` before building (CLI or CI), for example `BUILD_CHANNEL=press-ign`. If that build leaks, its reports carry the channel and build ID; match them against `GameProtectionBuildLedger.csv`.

### Optional dependencies

| Package | What turns on | How |
|---|---|---|
| Steamworks.NET (`com.rlabrecque.steamworks.net`) | Steam App ID check, Steam ticket proof for Content Lock | Automatic for a Package Manager install; otherwise the validator offers a one-click define |
| Addressables (`com.unity.addressables` 1.19+) | Encrypted AssetBundle Provider, automatic bundle encryption after content builds | Automatic |
| Google Play Integrity plugin | Android proof for Content Lock Remote mode | Import the sample |
| BitMono (`com.bitmono.unity`) | Obfuscation validation, metadata encryption toggle, Unity-safe exclusions | Automatic for the UPM .tgz; one-click define for the .unitypackage |
| Firebase Analytics | Integrity telemetry | Import the sample |

---

## Architecture

```
Detection                                   Content Lock
IIntegrityCheck (Strategy)                  ContentEncryptor / DecryptingStream (AES-256-CTR, seekable)
   └─▶ IntegrityService (pure C#)              └─▶ EncryptedAssetBundleProvider (Addressables) / ContentLock.LoadBundle
         └─▶ IntegrityReport                 IContentKeyProvider (Strategy): LocalKeyProvider | RemoteKeyProvider
               ├─▶ IntegrityEvents (Observer)       └─▶ IEntitlementProofProvider: Steam ticket | Play Integrity | dev
               ├─▶ IIntegrityReporter                     └─▶ DeviceBoundKeyCache (offline after first launch)
               └─▶ ITamperNoticePresenter          Server~/cloudflare-worker (verifies proofs, hands out keys)
```

Every optional integration is its own assembly, gated by a define, so the core never depends on anything. Adding a check, a reporter, a proof provider or a notice never changes existing code.

## Honest limits

- No client-side protection is unbreakable, and in 2026 breaking it got cheap: AI agents reconstruct native game code overnight, and free runtime inspectors for Windows Unity games read any object out of a running copy (one ships an MCP server, so an agent can drive it). The goal is a client that holds nothing worth recovering, plus evidence for when it is copied anyway.
- Remote mode's weak point is the key, not the cipher. A legitimate buyer can read the key ring from memory, and a leaked key opens every bundle for every clone. Rotate keys with content updates and watch the server log for grants from unexpected channels; this is where a determined thief goes.
- Obfuscate your code separately (the guide lists tools). BitMono's Unity integration renames `Assembly-CSharp` only, so your own asmdefs and this kit's runtime assembly keep their type names unless you run `BitMono.CLI` on them. Metadata encryption stops static dumpers, not memory dumps.
- Local mode's key can be extracted by anyone who reads this repository. Use Remote mode for anything you'd mind losing.
- Encrypted Addressables bundles served over HTTP are downloaded every session (Unity's bundle cache can't see through the container), and on Android a bundle in StreamingAssets is held in memory twice while it loads. Keep encrypted remote groups small or ship them locally.
- Nothing here stops a clone made from your trailer. That is the most common theft of 2026 and it needs priority evidence, a store page and a trademark, not code. See Layer 0 of the guide.

## Read next

- [Protection guide](Documentation~/GUIDE.md): the full strategy, which obfuscator to use, demos, China, accounts.
- [Takedown playbook](Documentation~/TAKEDOWNS.md): what to do when you find a copy, including Chinese platforms.
- [Key server](Server~/cloudflare-worker/README.md): deploy and configure Remote mode.

## Contributing

New checks, proof providers (Epic, GOG, consoles), asset watermarking, notice UIs, translations and real takedown stories are all welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE.md)
