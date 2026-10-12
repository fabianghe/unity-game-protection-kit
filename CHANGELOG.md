# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

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
