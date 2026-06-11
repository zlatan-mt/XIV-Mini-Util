# Test Boundaries for Refactoring

This note separates code that can be validated in `tools/CharaSelectLogicTests` from code that still requires Dalamud / FFXIV runtime verification.

## Pure or Near-Pure Logic

These areas should prefer console-runner tests before behavior changes:

- `Configuration` serialization compatibility: top-level property names, enum values, migration defaults, and `ApplyFrom` copy behavior.
- CharaSelect planning helpers: `CharaSelectSceneCompositionPlanner`, profile resolution, route verdicts, next-action text, and resolver inputs represented as plain candidates.
- TitleBackground QuickCheck policy: evaluator results, warning / NG classification, and diagnostic line presence.
- Diagnostic text builders that only transform snapshots into `key=value` lines.
- Shop / item data shaping when inputs can be represented as in-memory rows or DTOs without Dalamud services.

## Runtime-Bound Logic

These areas must be checked in-game or with captured runtime diagnostics after structural changes:

- Dalamud command registration and ImGui draw order.
- `IGameInteropProvider` hooks, detours, hook enable / disable timing, and hook dispose order.
- FFXIVClientStructs pointers, `AgentLobby`, `Character`, `LayoutWorld`, `EmoteManager`, and `PlayerState` reads.
- TitleBackground native adapter state, scene generation timing, and `/xmutbgdiag` source / verdict fields.
- CharaSelect prefetch behavior, login-wait hook behavior, and actual character select visual results.

## Refactor Gate

For structural changes, the default gate is:

1. `scripts/verify-refactor-phase.ps1`
2. Focused grep for forbidden TitleBackground camera / actor write patterns when TitleBackground files are touched.
3. Real-game `/xmu diag`, `/xmutbgdiag`, and `/xmutbgcheck` comparison when hook, pointer, scene, or diagnostic-key behavior is affected.
