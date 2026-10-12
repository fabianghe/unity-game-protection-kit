# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

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
