# Contributing

Thanks for helping indie devs protect their work.

## Principles (please read before opening a PR)

1. **Never punish legit players.** A check returns `Fail` only on evidence a real player can never produce. Anything uncertain is `Inconclusive`. Nothing in this package blocks, crashes or degrades the game.
2. **Private by default.** The package never sends data anywhere. Reporting is always the developer's own `IIntegrityReporter`.
3. **Drop-in.** No required dependencies. Optional SDKs are detected through asmdef `versionDefines`, never hard-required.
4. **Simple but solid.** Small classes, one job each (SOLID), patterns only where they pay for themselves (Strategy for checks, Observer for results). No frameworks.
5. **Do no harm to the build.** Until a project runs Setup, the package must not fail or modify its builds.

## Good first contributions

- New checks: iOS (re-sign or provisioning detection), Facepunch.Steamworks, Epic Online Services, GOG Galaxy, console platforms.
- Validator rules for common mistakes, and detection for more obfuscators.
- Robust asset watermarking (textures and audio) that survives compression and re-import.
- Notice UI variants (UI Toolkit, localized text).
- Guide improvements and anonymized takedown stories in `Documentation~/`.

## Development setup

1. Create (or use) a Unity 2021.3+ project.
2. Clone this repo into the project's `Packages/` folder. Unity picks it up as an embedded package.
3. To run tests, add the package to `testables` in `Packages/manifest.json`:
   ```json
   "testables": ["com.fabianghe.game-protection-kit"]
   ```
   then open **Window → General → Test Runner → EditMode**.

## Optional integrations

Anything that depends on a third-party package lives in its own assembly under `Runtime/Integrations/<Name>/` (and `Editor/<Name>/` for editor code), gated with `defineConstraints` and a `versionDefines` entry for the package. The core assemblies never reference it. The module registers itself with `[RuntimeInitializeOnLoadMethod]` (`GameProtectionKit.RegisterCheckFactory`, `ContentLock.ProofProvider`) or `[InitializeOnLoad]` (`ContentLockEditor.ExtraRules`). See `Runtime/Integrations/Steam` for the pattern.

Proof providers for other stores are the most wanted contribution: Epic Online Services, GOG Galaxy, Xbox/PlayStation/Switch. Each needs a client `IEntitlementProofProvider` and a verifier in `Server~/cloudflare-worker/worker.js`.

## Adding a check

1. Add a class in `Runtime/Checks/` implementing `IIntegrityCheck`, with a `public const string CheckId`.
2. Guard platform-specific code with `#if` and return `Inconclusive` elsewhere.
3. Add any settings to `IntegrityConfig` with a tooltip, and wire the check in `GameProtectionKit.CreateChecks`.
4. Add a validator rule if a missing setting would silently disable the check.
5. Add EditMode tests. Use an internal constructor as a test seam for platform APIs (see `WebHostCheck`).
6. Update `README.md` (checks table) and `CHANGELOG.md`.

## Pull requests

- One feature or fix per PR.
- Tested in at least one real build on the platform you touched, not just in the editor. Say which Unity version and platform.
- Every new file needs a `.meta` file with a fresh GUID. Unity creates these when the package is embedded in a project, so commit them.

## Code style

- C# 9 features that Unity 2021.3 supports are fine.
- Namespaces: `GameProtection`, `GameProtection.Checks`, `GameProtection.Editor`.
- Comments explain *why*, not *what*.
