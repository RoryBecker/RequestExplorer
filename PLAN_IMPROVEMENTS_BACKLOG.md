# RequestExplorer — Improvements Backlog Plan

**Status:** Phases A0–B and D approved; C / E–G awaiting approval  
**Created:** 2026-10-02  
**Updated:** 2026-10-05 (Phase D approved)  
**Companion to:** [`PLAN_COMPLETED.md`](PLAN_COMPLETED.md), [`PLANNED_ARCHITECTURAL_REFACTOR.md`](PLANNED_ARCHITECTURAL_REFACTOR.md)

**Repo policy:** Do **not** auto-commit. The human owns all commits and confirms code changes via commits. Implement only after explicit phase approval.  
**This document:** Planning only — source code must not change until a phase is approved.

---

## Purpose

Turn the day’s improvement notes into an ordered, implementable plan: current-state findings, recommended approach, risks/gaps, and open questions that need your answers before coding.

---

## Decisions (2026-10-02)

| # | Topic | Decision |
|---|--------|----------|
| 1 | Promote / move to variables | Right-click menus listing **specific named targets** to move **up** or **down** the hierarchy (not a generic picker dialog as primary UX). |
| 2 | Inherited variables | Show **read-only inherited** variables in the Variables UI when possible. |
| 2b | Grid sort + blank row crash | **Bug:** sorting a grid that still has a blank spare row, then saving, appears to crash. Track as high-priority correctness fix. |
| 3 | Per-request response | Session-only. **No** persistence of responses across app restarts. |
| 4 | Modify as JSON | **Internal** model JSON for now; apply to **selected subtree** only. |
| 5 | Dirty / Ctrl+S | **Both:** PropertyChanged (dirty while typing) **and** Ctrl+S commits focused grid/editor before save. |
| 6 | Backups | **Both** rolling backup **and** restore UI; settings for retention N (and related prefs). |
| 7 | Timeout | Global setting: default **30 seconds**; **dev mode** = infinite timeout. |
| 8 | Replay mode | **Parked.** Remains in backlog; do **not** implement without express instruction. |
| 9 | Fidelity analysis | Clarified below — optional investigation phase producing a gap report (not a product feature). |

---

## Suggested delivery order

Phases are ordered by **user pain / correctness first**, then **editor power features**, then **larger new surfaces**. Adjust freely after review.

| Phase | Theme | Items |
|------:|-------|--------|
| A0 | Hotfix ✅ | Grid sort + blank spare row → save crash |
| A | Correctness / save reliability ✅ | Dirty-while-typing; Ctrl+S commits focused grid; per-request response retention (session-only) |
| B | Send / response UX ✅ | Global timeout 30s + dev-mode infinite; response tabs flood panel; response body search; download when document available; raw request/response tabs |
| C | Variables workflow | Right-click move up/down to named targets; read-only inherited variables; variable glyph + hover |
| D | Editor layout / grids ✅ | Body as first tab; copy/paste grid rows across areas; remove left/right splitter MaxWidth |
| E | Data safety | Rolling backups + restore + settings dialog (N, timeout, dev mode) |
| F | Advanced tools | Modify-as-JSON (internal, selected subtree). **Replay excluded unless re-approved.** |
| G | Investigation deliverables | Fidelity gap analysis (save/load/export/import) — see explanation; window-move lag root-cause |
| H | Optional structural | Touch points that overlap [`PLANNED_ARCHITECTURAL_REFACTOR.md`](PLANNED_ARCHITECTURAL_REFACTOR.md) only if a feature forces it |
| Z | Parked | Replay mode — backlog only |

---

## Cross-cutting principles

1. **Undo first:** Structural and field edits that already use `UndoStack` should keep doing so; new promote/move/grid-paste/JSON-apply operations should be undoable (composite where needed).
2. **Dirty + persist:** Anything that changes collection data must mark dirty immediately enough that Ctrl+S works; session-only UI state (response search query, replay count) should not dirty collections unless you decide otherwise.
3. **Filter / selection restore:** Tree mutations from JSON replace should reuse existing reveal/select patterns where practical.
4. **No silent data loss:** Promote, demote, JSON replace, and backup restore need clear confirmation when destructive.
5. **Postman fidelity:** New features that touch persistence should be checked against import/export round-trip (Phase G feeds this).

---

## Item catalogue

### 1. Promote param / header to variable (and general move to/from variables)

**Intent:** Quickly move values between params/headers and variables, and between variable scopes up/down the tree.

**Decision:** Primary UX is **right-click menus** with concrete actions aimed at **specific named targets** (e.g. “Move up to folder ‘Auth’”, “Move down to request ‘Login’”), not a free-form picker as the main path.

**Current state:**
- Params/headers are `KeyValueItemViewModel` lists on the request.
- Variables live on container nodes (`ContainerNodeViewModel`) and resolve via `VariableResolver.BuildScope` (ancestor chain).
- No promote/demote command today.

**Proposed approach:**
- On param/header row context menu:
  - **Promote to variable on \<AncestorName\>…** for each ancestor container (folder/collection), creating/updating a variable and replacing the cell with `{{name}}` (confirm name default = key).
- On variable row context menu:
  - **Move up to \<AncestorName\>** / **Move down to \<ChildName\>** listing legal named targets.
- All moves undoable via `CompositeUndoAction`.

**Gaps / risks:**
- Name collisions at target scope.
- “Move down” target list can be large under big folders — may need submenu by child.
- Request-level variables may not exist; promote targets are containers unless we add request vars later.

**Still open (minor):** After promote, always replace cell with `{{name}}`? Assumed **yes** unless you say otherwise.

---

### 2. Copy/paste grid items from area to area

**Intent:** Copy rows between Params, Headers, and Variables grids (and possibly Auth-related key/value UI if applicable).

**Status:** Implemented (Phase D, 2026-10-03).

**Fix shipped:**
- Shared `KeyValueClipboardPayload` (internal JSON + TSV fallback).
- Ctrl+C / Ctrl+V on Params/Headers/Variables grids (skips when editing a cell); context menu Copy/Paste rows.
- Paste appends before the spare row; cross-area paste allowed; undoable as a composite batch.

---

### 3. Show calculated (inherited) variables from higher levels

**Intent:** When editing a node’s Variables UI, also show variables contributed by ancestors as **read-only** rows.

**Decision:** Yes — show read-only inherited variables when possible. (Optional hide-toggle can follow; not required for v1.)

**Current state:**
- Each container shows only its own variable rows.
- Resolution already walks ancestors at send time; UI does not surface the merged view.

**Proposed approach:**
- Variables editor: **Local** (editable) + **Inherited** (read-only: name, value, source node name/path).
- Shadowing badge when a local key overrides an ancestor.
- Optional later: click inherited row → navigate to defining node; prefs toggle to hide inherited section.

**Gaps / risks:**
- Large trees → many inherited rows; group/collapse by source if needed.

---

### 4. Promote body to first tab on request

**Intent:** Reorder request editor tabs so Body comes before Params (or becomes the default selected tab).

**Status:** Implemented (Phase D, 2026-10-03).

**Fix shipped:**
- Tab order: Body, Params, Headers, Auth, Raw Request.
- `ToTabIndex` / undo reveal mapping updated; default selected tab is Body (index 0).

---

### 5. Enhance to download document when one is available

**Intent:** When the response is a downloadable document (file attachment / binary / non-text), offer Save/Download.

**Status:** Implemented (Phase B, 2026-10-02).

**Fix shipped:**
- Response bytes retained session-only; **Save body…** on the response strip when bytes exist.
- Downloadable (attachment / binary) boldens the button; suggested filename from Content-Disposition / URL / content-type.

---

### 6. Each request retains its most recent response (or null); switching nodes restores that response

**Intent:** Response panel is per-request, not global. Switching away and back shows that request’s last response (or empty if never sent / cleared).

**Decision:** **Session-only.** No persistence of responses across sessions.

**Status:** Implemented (Phase A, 2026-10-02).

**Fix shipped:**
- `RequestNodeViewModel.LastResponse` holds session-only state.
- Response panel binds to `SelectedRequest.LastResponse`.
- Send/diagnosis write to the captured request’s slot (safe if selection changes mid-flight).

---

### 7. Response tabs flood the lower panel

**Intent:** Response tab content should use the full lower pane (less chrome / wasted margin / header stacking).

**Status:** Implemented (Phase B, 2026-10-02).

**Fix shipped:**
- Compact status strip (no large “Response” title); tighter host margins; tabs fill remaining height.

---

### 8. Add search panel to response body

**Intent:** Find-in-body for response text (and maybe highlight count / next-prev).

**Status:** Implemented (Phase B, 2026-10-02).

**Fix shipped:**
- Body tab Find box with Next/Prev and match count; selects and scrolls to the current match (case-insensitive).

---

### 9. Modify-as-JSON context panel

**Intent:** Show the selected hierarchy as JSON; edits apply back by replacing that subtree.

**Decision:** **Internal** disk/model JSON (not Postman export shape). Scope = **selected subtree** only.

**Current state:** Quick Export shows export text; Postman mapper is a different shape; local persistence uses internal models.

**Proposed approach:**
- Context command: **Edit as JSON** on selected collection/folder/request.
- Panel with editable internal JSON for that subtree.
- Apply: validate → replace selected subtree only → one undo composite → dirty.
- Reject partial applies on parse/validation errors.

**Gaps / risks:**
- Data loss if user pastes invalid structure — confirm dialog on Apply.
- Sync spare rows / in-progress edits before serialize.

---

### 10. Ctrl+S while in a grid commits the cell first

**Intent:** Ctrl+S must not leave an uncommitted DataGrid/TextBox edit behind.

**Decision:** Do this **and** PropertyChanged dirty-while-typing (item 15). Both.

**Status:** Implemented (Phase A, 2026-10-02).

**Fix shipped:**
- `EditorCommitHelper` commits titles, DataGrid cell/row edits, and focused TextBox bindings.
- Invoked on Ctrl+S preview and via `PendingEditCommitRequested` before Save / Save All.

---

### 11. Rolling backup feature

**Intent:** On startup and on demand, copy app data to a rolling backup set; keep last N (default 15); N configurable in a settings dialog.

**Decision:** **Both** backup and restore. Settings dialog hosts retention N (and can also host timeout / dev mode from item 12).

**Current state:**
- Collections live under LocalAppData via `CollectionPersistenceService`.
- Preferences in `preferences.json`.
- No backup/restore UI.

**Proposed approach:**
- Backup root e.g. `%LocalAppData%\RequestExplorer\backups\yyyyMMdd-HHmmss\`.
- Copy collections + preferences + collection-order.
- Startup: backup then prune to N.
- Menu: **Backup now**, **Restore…**, **Open backups folder**.
- Restore: confirm, then replace workspace from chosen backup (detail: full workspace restore for v1 unless narrowed later).
- Settings: `BackupRetentionCount` (default 15), backup-on-startup checkbox; same dialog for global timeout + dev mode.

---

### 12. Timeout + global dev mode

**Intent:** Controllable global send timeout; infinite when developing against slow endpoints.

**Decision:**
- Default timeout: **30 seconds** (change from current 100s).
- **Dev mode** (global): infinite timeout; Cancel remains the stop mechanism.
- Settings are **global** (not per-request).

**Status:** Implemented (Phase B, 2026-10-02).

**Fix shipped:**
- Prefs: `RequestTimeoutSeconds` (default 30), `DevModeIgnoreTimeout`.
- **Tools → Settings…** dialog; executor applies infinite timeout in dev mode.

---

### 13. Replay mode — fire events repeatedly — PARKED

**Intent:** Send the same request repeatedly for smoke/load checks.

**Decision:** **Do not implement** without express instruction. Remains in backlog (Phase Z) only.

**Notes for later:** sequential loop, delay, stop/cancel, summary stats; session-only; no collection dirty.

---

### 14. Fidelity analysis (what “item 9” / Phase G means)

**This is not a UI feature.** It is an investigation you originally asked for:

> Ask specifically for an analysis of anything needed for a request which is not being saved and/or loaded or exported/imported.

**Plain-language meaning:**  
Walk every piece of data you can set on a request (URL, params, headers, body modes, auth types, etc.) and check whether each piece survives:

1. **Save to disk** and reload in RequestExplorer  
2. **Export** to Postman JSON  
3. **Import** back from Postman JSON  

Some fields may work in the editor but get dropped on save, or save locally but vanish on Postman export/import (e.g. historically suspicious: form-data, oauth1, some body/auth variants).

**Deliverable:** a report (e.g. `FIDELITY_GAPS.md`) with a table:

| Field / surface | Local save/load | Postman export | Postman import | Notes |
|-----------------|-----------------|----------------|----------------|-------|

**Then:** you choose which gaps to fix. Analysis alone changes no product behaviour.

**Status:** Still available as Phase G when you want it; not blocking other phases.

---

### 15. Dirty only recognised when a field is left (blocks Ctrl+S)

**Intent:** Typing in a field should dirty immediately; Ctrl+S must still commit + save.

**Decision:** **Both** PropertyChanged bindings and Ctrl+S commit safety net.

**Status:** Implemented (Phase A, 2026-10-02).

**Fix shipped:**
- Params/Headers/Variables Key/Value and Body fields use `UpdateSourceTrigger=PropertyChanged`.
- Undo coalescing (`ICoalescingUndoAction`) merges consecutive key/value/body/variable text edits into one stack entry.

---

### 0. Hotfix — grid sort with blank spare row then save crashes

**Intent:** Sorting a params/headers/variables grid that still has the blank trailing row, then saving, must not crash.

**Reported:** Side note from user (2026-10-02).

**Status:** Implemented (2026-10-02).

**Fix shipped:**
- Custom spare-aware grid sort (`KeyValueGridSorting`): blank-key rows always last; disabled keyed rows park after enabled; column sort reorders the real list and syncs the model.
- Hardened spare-row helper: collapse fully blank mid-list rows; ensure a single trailing spare.
- Save path calls `NormalizeKeyValueSpares` before persist.

---

### 16. Remove limit on splitter between left and right

**Intent:** Allow the collections tree column to grow/shrink beyond the current cap.

**Status:** Implemented (Phase D, 2026-10-03).

**Fix shipped:**
- Removed `CollectionsColumn` `MaxWidth` and the 520 clamp in prefs restore; `MinWidth` 220 kept.

---

### 17. Window move delay — are data operations triggered?

**Intent:** Diagnose jank when dragging the window.

**Current state (snapshot):**
- `LocationChanged` → `CaptureWindowBounds()` only updates **in-memory** `UiPreferences` fields (no disk write per move).
- Disk persist (`PersistUiPreferences`) runs from splitter drag completed, quick-export visibility changes, etc. — **not** from every `LocationChanged` in the current code.
- So lag is **unlikely** to be collection save/load; more likely WPF render/layout, DWM, large visual tree (TreeView), or something else hooked indirectly.

**Proposed approach:**
- Profile: temporarily log/timestamp `LocationChanged`, `CaptureWindowBounds`, `PersistUiPreferences`, filter apply, property storm.
- Check for bindings on `Left`/`Top`, expensive `OnPropertyChanged` cascades, or non-virtualized tree cost while dragging.
- If prefs save was intended on move: debounce heavily (e.g. save on `StateChanged` / close only).

**Deliverable:** short root-cause note + fix only if a real handler is at fault.

**Questions:** Q28.

---

### 18. Tabs for raw request and raw response

**Intent:** Show wire-level (or near wire-level) request and response text.

**Status:** Implemented (Phase B, 2026-10-02).

**Fix shipped:**
- **Raw Request** tab on the request editor (top); **Raw Response** on the response panel (HttpClient view; captured per request at send time).

---

### 19. Glyph on nodes that define variables; hover shows them

**Intent:** Tree affordance for “this node has variables”; tooltip lists them.

**Proposed approach:**
- Small glyph in tree row template when `Variables` has any named row (or any enabled row — TBD).
- ToolTip: name=value lines (truncate long values; mask secrets? TBD).
- Update when variables change / undo / import.

**Gaps / risks:** Tooltip with secrets; performance if tooltips rebuild often.

**Questions:** Q30–Q31.

---

## Phase G — Fidelity analysis (requested specifically)

**Output:** `FIDELITY_GAPS.md` (or a section appended here after investigation).

**Method:**
1. Inventory all request editor fields and container fields.
2. Trace each through `CollectionTreeMapper.SyncModelFromView` / load path.
3. Trace through `PostmanCollectionMapper` export and import.
4. Smoke against `SamplePostmanExports` where relevant.
5. Classify: Full / Partial / Dropped / UI-only.

**Out of scope for analysis-only:** implementing fixes (track as follow-up tickets in this plan).

---

## Dependency sketch

```text
[A dirty/save + grid commit] ──┬──► most editor features safer
[A per-request response] ──────┼──► raw tabs, replay, download UX
[B timeout settings] ──────────┘
[C variables promote/inherited/glyph] ◄── shared variable UX
[D body tab + splitter]               ◄── small, parallelizable
[E backups + settings dialog]         ◄── introduces Settings surface reused by timeout/N
[F JSON edit + replay]                ◄── after save reliability + fidelity notes
[G fidelity + move-lag investigation] ◄── can start anytime (read-only)
```

**Recommendation:** Do **G (fidelity)** and **A (dirty/save + per-request response)** early; introduce a minimal **Settings** dialog as part of **E** (backups) and immediately reuse it for timeout retention (B/E overlap).

---

## Remaining open questions (optional — “your call” is fine)

Answered items removed. Still useful before the related phase:

- **Q2.** After promote to a named target, always replace the grid cell with `{{varName}}`? *(Assumed yes.)*
- **Q3.** Move-to-variable menus on Auth fields / Body, or only Params/Headers/Variables for v1?
- **Q4–Q5.** Grid copy/paste matrices and insert vs append.
- **Q7.** Body first tab = also default selected tab when opening a request?
- **Q8–Q9.** Download offer rules; large-response memory vs temp file.
- **Q11.** LRU cap for session responses, or keep all until restart?
- **Q12–Q13.** Response chrome reduction; search v1 = plain next/prev?
- **Q16.** Confirm dialog on JSON Apply?
- **Q27.** Splitter: remove MaxWidth entirely vs raise high?
- **Q28–Q31.** Move-lag instrumentation OK?; raw = last-send snapshot?; glyph on any keyed var vs enabled-only?; mask secret-looking tooltip values?

---

## Approval checklist

Before implementation starts, please:

1. Confirm or reorder phases (**A0 → A → …**; Replay = Z parked).
2. Call out any item to **drop**.
3. Explicitly approve the first phase to implement (recommended start: **A0** crash fix).

No source code will be changed until that approval.
