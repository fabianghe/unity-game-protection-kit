# Takedown playbook

What to do when you find a stolen copy. This is practical experience, not legal advice. If real money is at stake, talk to a lawyer.

## Before it happens (do this now)

Keep an **evidence folder** per game:
- your source repository with dated commits (export the log occasionally);
- `GameProtectionBuildLedger.csv` and your ownership token from this kit;
- dated devlogs, trailers, store page archives, press mentions;
- original art source files (PSD, Aseprite, Blender) with their timestamps.

Embed **visible authorship** in the game: a splash or credit screen with your name and official link.

In most countries copyright applies automatically. A **trademark** on your game or studio name costs money, but it makes takedowns on mobile and regional stores much easier. For example, an EU trade mark is filed through EUIPO.

## When you find a copy

1. **Don't engage the uploader publicly** and don't announce it before filing. Some delete and re-upload under a new name, and some retaliate with false claims against you.
2. **Scan the copy.** Extract it with AssetRipper or AssetStudio (in a VM), then run Tools > Game Protection > Scan Suspicious Build. A found ownership token or build ID is the strongest evidence you can attach.
3. **Capture everything:** screenshots of the store page, the URL, the developer name, prices, and dates. Download the copy if it's safe to do so (in a VM; it may contain malware) and confirm your build stamp, credits or unique strings are inside.
4. **File with the platform** through its official copyright or IP infringement process. Steam, Google Play, the App Store and itch.io all have one. Be specific: what is yours, where it is, and how you can prove it.
5. **Report to the community.** Players on the store's discussion boards often help speed things up, and other affected developers may already be filing against the same uploader.
6. **Check for siblings.** Thieves rarely steal one game. Look at the uploader's other titles and notify those developers.
7. **Follow up.** Platforms can be slow. Reply to the same ticket rather than opening new ones.

## A clone made from your trailer (no code or assets taken)

Lookalikes built from a viral clip, sometimes with AI, contain none of your files, so the scanner finds nothing. The case is priority and likeness instead:

1. **Collect priority evidence**: your dated Steam page, devlogs, the first public post of the clip, and the build ledger with its external timestamp if you kept one. Platforms weigh dates heavily when two games look alike.
2. **Document the likeness** side by side: capsule, screenshots, trailer frames, UI, names and text. Copyright protects expression (art, text, music, specific layouts), not the mechanic, so lead with what was copied verbatim.
3. **File with the store.** Steam acts on complaints, not on its own; be specific and attach the evidence. If the clone uses your name or a confusable one, a trademark registration turns this into a routine takedown.
4. **Extortion** ("pay or I publish the clone") goes to the platform and, if the amount matters to you, to the police. Don't pay and don't negotiate; keep the messages.
5. **Say nothing public until it's filed.** Afterwards, a calm post pointing players at the original is marketing you didn't have to pay for.

## Chinese platforms (Douyin / WeChat mini-games, TapTap, 4399 and others)

Expect this to be slower and less reliable than Steam or Google Play, and plan for it:

1. **Evidence first.** A CPCC copyright registration certificate (see the guide) is the document their complaint portals accept most readily. Add a Chinese translation of your ownership statement and the scanner report.
2. **Use the platform's infringement email or form**, in Chinese if you can (machine translation is fine). Douyin commits to acting within seven business days of a valid notice. Keep the ticket number and follow up on the same thread.
3. **Players are your allies.** The in-game notice from this kit, and a visible mechanic that marks the real game, make players report and abandon the clone.
4. **Don't expect money.** Civil action in China against a free ad-supported clone is rarely worth it for a solo developer. Content Lock keeps the next clone from working; that is the better investment.

## Afterwards

- Note which build leaked (channel and build ID) and tighten that channel.
- Share your story (anonymized if you like) in this repo's Discussions so others learn from it.
