# Game Protection Kit for Unity

**Protect any Unity game from theft: detect stolen, re-uploaded and repackaged copies, ship builds that are harder to rip, and prove ownership when a clone appears.**

Indie games and demos are being decompiled, extended (increasingly with AI), and re-uploaded to Steam, mobile stores and web portals, sometimes with ads injected or malware attached. No client-side protection is unbreakable, and this kit doesn't pretend otherwise. It makes your game a worse target than the one next to it, catches the lazy majority of thieves automatically, and keeps the mistakes that make theft easy out of your builds.

- ✅ Drop-in: install from a git URL, click **Setup**, done. No code required.
- ✅ Never punishes real players: no DRM, no crashes, no lockouts.
- ✅ Private by default: sends nothing anywhere unless you plug in your own analytics.
- ✅ Works without any third-party SDK. Steamworks.NET and Firebase are optional.

---

## What it does

### At runtime (detection)

| Check | Catches | Platforms |
|---|---|---|
| App identity | Re-uploads with a different package name, product or company name | All (identifier: Android/iOS) |
| Android signature | Repackaged APKs: injected ads, "mods", store re-uploads | Android |
| Android installer | Where copies are installed from (reported, never fails) | Android |
| Steam App ID | Builds re-uploaded under another App ID, demos sold as full games | Windows/macOS/Linux + Steamworks.NET |
| Web host | WebGL builds rehosted on portals you didn't approve | WebGL |
| Your own | Anything: implement `IIntegrityCheck`, one method | Any |

Results go to:
- your analytics, so you learn a clone exists, often before anyone tells you;
- an optional in-game notice that tells players of a stolen copy where the official game lives.

### At build time (prevention)

- **Demo leak guard.** Demo builds fail if they contain a non-demo scene, a scene that references full-game assets, or full-game content inside `Resources`. Locked content is still shipped content.
- **IL2CPP enforcement.** Release builds fail on the Mono backend, which ships your C# in a form that decompiles back to near-original source.
- **Build stamping.** Every build gets an ID and a channel (`full`, `demo`, `press-outlet`, `discord-testers`…). Each one is logged to `GameProtectionBuildLedger.csv`, so a leaked copy tells you exactly where it came from.
- **Ownership canary.** A permanent, project-unique token goes into every build's data and into a hidden object in every scene. Asset rippers carry it into rebuilt clones without the thief knowing it matters.
- **Obfuscation check.** The validator warns when release builds have no obfuscator or metadata protection.
- **"DoNotShip" cleanup.** IL2CPP's C++/symbols backup folder and Burst debug info are moved out of the build output, so they never get uploaded to Steam. They're kept for crash symbolication.
- **Validator.** Catches config drift, like a renamed product that would flag your own players, before it ships. Every issue comes with a one-click fix.

### For takedowns (evidence)

**Tools → Game Protection → Scan Suspicious Build…** takes a folder containing a suspected copy, ideally extracted first with AssetRipper or AssetStudio. It searches for your ownership token and every build ID in your ledger, then writes a dated report to `GameProtectionEvidence/` that you can attach to a copyright claim.

---

## "Isn't IL2CPP enough?"

No. Plenty of stolen games already used IL2CPP. There are two main ways games get taken:

1. **Re-uploaded as-is.** Nothing is decompiled: the thief uploads your build under a new name. No code protection matters here, which is why the kit focuses on **detection and evidence**.
2. **Decompiled, extended, re-released.** Public tools dump IL2CPP metadata back into readable class and method names, and asset rippers extract every scene and asset whatever the scripting backend. What raises the cost is the combination:
   - obfuscated names,
   - an encrypted `global-metadata.dat`, so the dumpers fail,
   - a demo that doesn't contain the full game.

   In a clone rebuilt this way your scripts get rewritten, so runtime checks are gone, but your data survives. That's what the ownership canary and the scanner are for.

See the [protection guide](Documentation~/GUIDE.md) for the full picture.

---

## Install

**Package Manager → + → Add package from git URL…**

```
https://github.com/fabianghe/unity-game-protection-kit.git
```

Pin a version with `#v0.1.0` at the end. Requires Unity 2021.3 or newer and is tested against Unity 6.

## Set up (2 minutes)

1. **Tools → Game Protection → Setup.** This creates both settings assets, pre-filled from Player Settings, and opens **Project Settings → Game Protection**.
2. **Copy your ownership token** (Release guards → Ownership canary) into your evidence folder. Never change it.
3. Fix anything the validator lists (most fixes are one click), and fill in what only you know:
   - **Official store URL**, shown to players of stolen copies.
   - **Android:** SHA-256 of the *App signing key* and *upload key* certificates, from Play Console → Test and release → App integrity.
   - **Steam:** your full game, demo and playtest App IDs.
   - **WebGL:** the hosts allowed to serve your game.
   - **Demo:** your demo scenes and the folders that must never ship in it.
4. For demos, add `GAME_DEMO` to your demo Build Profile's scripting defines (Unity 6), or to Player Settings.

That's it. Checks run automatically 20 seconds after the first scene loads, in builds only.

> Not set up yet? The package does nothing and never touches your builds until you run Setup.

---

## Customize (optional)

```csharp
using GameProtection;

// Where reports go (default: logs only). See the Firebase sample.
GameProtectionKit.Reporter = new MyAnalyticsReporter();

// What players of a stolen copy see (default: a simple dialog). See the UnityEvent sample for a no-code option.
GameProtectionKit.NoticePresenter = new MyNoticeUi();

// Add your own checks.
GameProtectionKit.RegisterCheck(new MyCustomCheck());

// React to results anywhere (Observer).
IntegrityEvents.ReportReady += report => Debug.Log(report);
```

Set these from your own bootstrap (`Awake`, or `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`).

**Samples** (Package Manager → Game Protection Kit → Samples):
- **Firebase Reporter** sends integrity signals to Firebase Analytics.
- **UnityEvent Notice** wires your own stolen-copy UI in the Inspector.
- **Custom Check** is a template for writing your own check.

### Writing a check

```csharp
public sealed class MyCheck : IIntegrityCheck
{
    public string Id => "my_check";

    public IntegrityResult Run()
    {
        // Fail ONLY on evidence a legit player can never produce. When in doubt, Inconclusive.
        return IntegrityResult.Pass(Id);
    }
}
```

### Per-recipient builds

Set `BUILD_CHANNEL` before building (CLI or CI), for example `BUILD_CHANNEL=press-ign`. If that build leaks, its reports carry the channel and build ID; match them against `GameProtectionBuildLedger.csv`.

### Steamworks.NET

If you installed it through the Package Manager, the Steam check is enabled automatically. If you installed it as a `.unitypackage`, the validator offers a one-click fix that adds the `GAMEPROTECTION_STEAMWORKS` define.

---

## Architecture

```
IIntegrityCheck (Strategy) ──▶ IntegrityService (pure C#) ──▶ IntegrityReport
                                                               │
GameProtectionKit (composition root) ── publishes ─────────────┤
                                                               ├─▶ IntegrityEvents.ReportReady (Observer)
                                                               ├─▶ IIntegrityReporter       (your analytics)
                                                               └─▶ ITamperNoticePresenter   (your UI)
```

Checks don't know about Unity lifecycle, reporting or UI. Adding a check, a reporter or a notice never requires changing existing code.

---

## Why open source, if thieves can read it?

The people re-uploading stolen demos don't audit packages on GitHub; they ship what they grabbed. Anyone determined enough to strip the checks could have stripped closed-source ones too. Open source lets every indie dev use this for free, review it, and make it better. Pair it with an obfuscator (see the guide) to raise the cost further.

## Read next

- [Protection guide](Documentation~/GUIDE.md): the full layered strategy (IL2CPP, obfuscators, demos, assets, accounts) and which tools to use.
- [Takedown playbook](Documentation~/TAKEDOWNS.md): what to do when you find a copy.

## Contributing

New checks, platforms (iOS hardening, consoles, Facepunch.Steamworks, Epic), better notices, translations, and real takedown stories are all welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE.md)
