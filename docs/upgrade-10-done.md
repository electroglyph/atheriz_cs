# .NET 10 / C# 14 upgrade — DONE (archived 2026-09-25 from `upgrade.md`, 351 lines)

- Baseline 2026-09-09: `net8.0`, `LangVersion 12.0`, SDK 8.0.130; suite 3747/3747 green.
- Target hit: .NET 10 SDK, TFMs `net10.0`, C# 14 (stable; `field` GA). Phases 1–6 done 2026-09-09/10.
- Gates held per phase; full gate green (5354/5354 by 2026-09-25).
- Standing exception (AGENTS.md): lock *types* may change where reentrancy is proven absent (was Phase 5); visibility never changes.
- Kept floats: `<LangVersion>latest</LangVersion>` (AGENTS.md), `global.json` `latestPatch`.
