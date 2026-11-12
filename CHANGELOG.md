# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- **The editor assembly did not compile** (`ProtectionValidator` called `Application.OpenURL` without the `UnityEngine` namespace), which took Setup, the validator, the build guards and every editor module with it.
- **Key rotation locked players out of new content in Remote mode.** A game with a still-fresh key cache never asked the server for the new key, so bundles encrypted after a rotation failed to load until the cache expired (30 days by default). Content now asks for the exact key it needs (`ContentLock.RequestKey`), which refreshes the ring once.
- **Addressables: bundles of unencrypted groups were encrypted too** when they shared a build folder with an encrypted group (the default layout), so demo groups and the shared shader bundle failed to load through the stock provider. Only bundles of groups on the Encrypted AssetBundle Provider are encrypted now.
- `ContentLock.LoadBundleFromFile`, `LoadBundleFromFileAsync` and `LoadBundle` never closed the encrypted file, so every loaded bundle kept a file handle for the rest of the session. The file is closed once its bundle is unloaded (`ContentLock.UnloadBundle` closes it immediately).
- A damaged key cache could be read as a ring of wrong keys, or throw. It is now authenticated (HMAC), written atomically, and anything unreadable counts as "no cache".
- `DecryptingStream` is safe to read from Unity's loading threads.
- `WaitForCompletion()` on an asset from an encrypted Addressables group threw "The ProvideHandle is invalid" (the handle was completed twice).
- Releasing an Addressables bundle that is still loading no longer closes the stream under the load.
- The stolen-copy notice is shown once per session, however often `RunNow` is called.
- **Steam module did not compile**: `SteamTicketProofProvider` used the runtime's internal coroutine host from another assembly, so any project with Steamworks.NET installed got a compile error from the kit.
- `RemoteKeyProvider` no longer wedges for the session when a proof provider throws or the key server answers with non-JSON: every waiting caller gets a result and the next request can retry.

### Added
- `ContentLock.RequestKey(keyId, callback)`, `ContentLock.UnloadBundle`, `ContentFormat.TryReadFileHeader`, `RemoteKeyProvider.RefreshForMissingKey`.
- Key server test suite (`npm test` in `Server~/cloudflare-worker`, no dependencies) covering nonces, the dev secret, Steam and Play Integrity.
- EditMode tests: AES-256-CTR against the NIST SP 800-38A vector, repeatable encryption, key cache integrity and upgrade.

### Changed
- **Encryption is repeatable.** The nonce is a keyed hash of the content instead of a random number, so a bundle that didn't change between two builds is byte-identical on disk. With random nonces every build rewrote every encrypted bundle and players re-downloaded all of them with each patch. Bundles encrypted by earlier versions still load.
- Key cache format (encrypted, then HMAC). Caches written by 0.3.0 are read once and upgraded, so updating never forces an offline player back online.
- The game honours the key server's `ttlSeconds` (the shorter of it and Cache Days), and a cache dated in the future no longer counts as fresh.
- Key server: nonces are 42 bytes, which encode to 56 base64url characters with no padding (Play Integrity takes a base64 nonce and echoes it back for comparison, so there is no padding to disagree about), request bodies are capped at 64 KB, and a missing `NONCE_SECRET` is reported as a configuration error. Existing games work with the new server unchanged.
- `package.json` declares the built-in Unity modules the kit uses, so it compiles in projects that have disabled them.
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
