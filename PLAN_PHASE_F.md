# Phase F — Advanced tools: Modify-as-JSON (plan)

**Status:** Approved (2026-10-05)  
**Created:** 2026-10-05  
**Updated:** 2026-10-05 (approved)  
**Parent backlog:** [`PLAN_IMPROVEMENTS_BACKLOG.md`](PLAN_IMPROVEMENTS_BACKLOG.md)  
**Repo policy:** Human owns all commits. Agent does not commit.

**Replay mode** remains **parked (Phase Z)** and is **out of scope** unless you explicitly re-approve it.

---

## Scope (from backlog Phase F)

| # | Item | Backlog ref |
|---|------|-------------|
| F1 | **Edit as JSON** for the **selected subtree** using **internal** model JSON | Item 9 + Decision #4 |
| F2 | **Apply** validated JSON back onto that subtree (undoable, dirty) | Item 9 |
| F3 | Surface UX (panel / dialog / command placement) | Item 9 |

**Out of scope for Phase F:** Replay (Z), Postman-shaped JSON editing, fidelity gap report (G), backups (E — already shipped).

---

## Decisions (2026-10-05)

| Q | Code | Decision |
|---|------|----------|
| Q1 | **A2** | **Edit as JSON** enabled only when selection count is **exactly 1**. |
| Q2 | **B1** | **Always confirm** before Apply (destructive replace). |
| Q3 | **C1** (+ UX rule) | Parse + basic structural checks; unknown JSON properties ignored. On parse/validation **failure**: show the error and **keep the dialog open** with the buffer intact so the user can **continue editing** until a **successful Apply** or **Cancel** (never force-close or wipe the buffer on error). |
| Q4 | **D1** *(provisional)* | Keep **original** root `id` on Apply (ignore payload id for the node being replaced). **Revisit later** — open between D1 vs D2. |
| Q5 | **E1** | If Apply changes a **collection name** and a disk file exists → **rename/sync on disk immediately** (same idea as tree rename). |
| Q6 | **F1** | **Modal dialog** with large editor + Apply / Cancel. |
| Q7 | **G3** | **Format** button + **simple find** in the dialog. |
| Q8 | **H1** | Closing with unsaved buffer edits → prompt **Discard / Keep editing**. |
| Q9 | **I1** | Children order after Apply = **exactly as in JSON arrays**. |
| Q10 | **J1** | Session state on replaced requests: **drop** (new VMs; no prior LastResponse). |
| Q11 | **K1** | After **successful** Apply → **close** the dialog. |
| Q12 | **L1** | Implement full F1–F3; nothing dropped. |
| Q13 | **M1** | No schema hint / sample stub — only real subtree JSON. |

Also locked from backlog:

| Topic | Decision |
|-------|----------|
| JSON shape | Internal RequestExplorer model JSON (not Postman export) |
| Scope | Selected subtree only |
| Undo | One undoable composite for Apply |
| Dirty | Apply marks owning collection dirty |
| Commits | Human only |
| Replay | Parked — not in F |

---

## Implementation notes

| Piece | Location |
|-------|----------|
| Serialize / deserialize / format | `RequestExplorer/Services/SubtreeJsonService.cs` |
| Modal editor (Format, Find, confirm, dirty close) | `RequestExplorer/Views/EditAsJsonDialog.xaml(.cs)` |
| Command + Apply replace + undo | `MainViewModel.EditAsJson` / `TryApplySubtreeJson` |
| Menus | Edit menu + tree context menu → **Edit as JSON…** |

Apply pipeline: confirm → deserialize (preserve root id) → build new VMs → `RemoveNodeCore` + `InsertNodeCore` → `CompositeUndoAction` → dirty / collection file sync → select new node → close dialog.

---

## Acceptance criteria

- [x] Edit as JSON enabled only for exactly one selected collection/folder/request.
- [x] Buffer is internal camelCase JSON for that subtree only.
- [x] Apply always confirms; success replaces subtree, one undo step, dirty, dialog closes.
- [x] Apply failure shows error and **leaves dialog open** with buffer intact for further edits.
- [x] Cancel/close with dirty buffer prompts Discard / Keep editing.
- [x] Format + Find available in the dialog.
- [x] Root id preserved on Apply (D1); collection rename syncs disk file (E1).
- [x] LastResponse not carried onto replaced request VMs.
- [x] Replay not implemented; no agent commits.

---

## Answer key (recorded)

| Q | Choice | Notes |
|---|--------|--------|
| Q1 | A2 | Exact selection count 1 |
| Q2 | B1 | Always confirm Apply |
| Q3 | C1 | + continue editing until success or cancel |
| Q4 | D1 | Provisional — revisit D1 vs D2 later |
| Q5 | E1 | Immediate disk rename on collection name change |
| Q6 | F1 | Modal dialog |
| Q7 | G3 | Format + find |
| Q8 | H1 | Discard / keep editing prompt |
| Q9 | I1 | JSON array order |
| Q10 | J1 | Drop session response state |
| Q11 | K1 | Close on successful Apply |
| Q12 | L1 | Full scope |
| Q13 | M1 | No schema stub |

---

## Open follow-up (not blocking)

- **Q4 revisit:** D1 (keep root id) vs D2 (accept payload id).

---

## Next step

Phase F approved. Human owns the commit. Phase G (fidelity gap analysis) awaits approval.
