# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- **Steam module did not compile**: `SteamTicketProofProvider` used the runtime's internal coroutine host from another assembly, so any project with Steamworks.NET installed got a compile error from the kit.
- `RemoteKeyProvider` no longer wedges for the session when a proof provider throws or the key server answers with non-JSON: every waiting caller gets a result and the next request can retry.

### Changed
- Ownership tokens are plain 32-character hex (no `GPK-` prefix) and the scene canary object name varies per project, so markers can't be found and stripped by shape. Existing tokens are unchanged.
- Validator: warns when `ContentLockKeys.asset` is tracked by git; detects LLObf as an obfuscator; BitMono messages say that metadata encryption blocks static dumpers only and that the kit's own runtime assembly stays unobfuscated.
- README and guide rewritten around the 2026 threat picture: AI-driven decompilation, runtime inspectors with MCP, and clones made from trailers (new Layer 0 in the guide, new section in the playbook).

## [0.3.0] - 2026-10-12

### Added
- **BitMono integration** (optional module, auto-detected): validates BitMonoConfig, one-click fixes to enable obfuscation and IL2CPP `global-metadata.dat` encryption, "Get BitMono" fix when no obfuscator is installed.
- **Unity-safe exclusions**: scans Animation Events (including clips inside models) and Inspector-wired UnityEvents, and merges the method names into BitMono's `criticals.json` between markers, idempotently, keeping BitMono's comments. Also available as `UnityNameScanner` for other obfuscators.
- Validator lists your own runtime assembly definitions that BitMono's Assembly-CSharp-only integration leaves readable.
- `ProtectionValidator.ExtraRules` for optional editor modules.

### Changed
- JSON DTOs (build stamp, key server responses) carry `[Obfuscation(Exclude = true)]` so any obfuscator leaves the wire format intact.

## [0.2.0] - 2026-10-12

### Added
- **Content Lock**: AES-256-CTR encryption of AssetBundles and Addressables bundles with on-the-fly decryption (`DecryptingStream`, `ContentEncryptor`, `ContentLock.LoadBundle`).
- Local mode (masked keys embedded at build time, never in demo builds) and Remote mode (`RemoteKeyProvider`: keys fetched after entitlement proof, cached device-bound, offline afterwards).
- Entitlement proofs: Steam Web API ticket (Steam module), Google Play Integrity (sample), dev shared secret (testing).
- Key server for Cloudflare Workers (`Server~/cloudflare-worker`) verifying Steam tickets and Play Integrity tokens; nonce-signed, fail-closed, tested.
- Addressables integration: `EncryptedAssetBundleProvider`, automatic encryption after every content build, one-click group switch, validation.
- Editor key store with rotation and "copy keys for server"; Content Lock section in Project Settings; folder encryption for plain AssetBundles.
- `GameProtectionKit.RunNow()` and `RegisterCheckFactory` for modules.
- Android installer source via `getInstallSourceInfo` on API 30+.

### Changed
- Steam support moved into an optional `GameProtection.Steam` assembly gated by `GAMEPROTECTION_STEAMWORKS`, so the core has no references at all.
- Validator no longer blocks a build when product/company names are intentionally left empty.
- Guide and playbook cover Chinese mini-game platforms and prevention-first strategy.

## [0.1.0] - 2026-10-12

### Added
- Runtime checks: app identity, Android signing certificate, Android installer (telemetry), Steam App ID (Steamworks.NET, auto-detected), WebGL host allowlist.
- Pluggable reporter (`IIntegrityReporter`), stolen-copy notice (`ITamperNoticePresenter`), custom checks (`GameProtectionKit.RegisterCheck`) and results event (`IntegrityEvents.ReportReady`).
- Build guards: demo leak protection (scenes, dependencies, Resources), IL2CPP enforcement, build stamping with ledger, DoNotShip folder cleanup.
- Ownership canary: permanent token in every build's stamp and scenes, survives asset ripping.
- Suspicious build scanner (Tools > Game Protection > Scan Suspicious Build...) that writes a dated evidence report.
- Validator warning when no obfuscator or metadata protection is detected.
- One-click setup (Tools > Game Protection > Setup) and Project Settings page with validation and fixes.
- Samples: Firebase reporter, UnityEvent notice, custom check template.
- Protection guide and takedown playbook.
