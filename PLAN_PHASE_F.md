# Phase F — Advanced tools: Modify-as-JSON (plan)

**Status:** Decisions recorded (2026-10-05) — awaiting explicit **implement** instruction  
**Created:** 2026-10-05  
**Updated:** 2026-10-05 (user answers)  
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

## Current state (code snapshot)

### Models (internal shape)

- `CollectionDefinition` / `FolderDefinition` / `RequestDefinition` (+ `KeyValueItem`, auth, body)
- Persistence: `System.Text.Json`, camelCase (`CollectionPersistenceService` options)

### Mapping

- `CollectionTreeMapper` bridges models ↔ tree VMs
- Commit pending edits + normalize spares before serialize; sync models before/after Apply

### Related UI

- Quick Export = export formats only (not this feature)
- Tree context menu has no Edit as JSON yet
- Undo / dirty / collection file rename helpers already exist

---

## F1 — Serialize selected subtree

### Behaviour (locked)

- Enabled iff **exactly one** selected node (**A2**) and it is collection / folder / request.
- Commit pending UI edits → normalize spares → sync model → pretty-print internal JSON (camelCase, indented).
- Root type matches selection kind.

---

## F2 — Apply JSON back

### Pipeline (locked)

1. **Confirm** always (**B1**).
2. Deserialize to expected type; **C1** validation (basic structure / enums; ignore unknown props).
3. On failure: **error UI, dialog stays open, buffer unchanged** — user keeps editing until success or Cancel (**C1 UX rule**).
4. On success: replace subtree only; preserve **original root id** (**D1** provisional); child order from JSON (**I1**); new VMs (**J1**); undo composite; dirty; collection name → disk sync if needed (**E1**); **close dialog** (**K1**).

---

## F3 — UX surface (locked)

- **Modal dialog** (**F1**): large editor, **Apply**, **Cancel**, **Format**, **Find** (**G3**).
- Tree context menu (+ optional Edit menu): **Edit as JSON…**
- Cancel / close with dirty buffer → Discard / Keep editing (**H1**).
- Successful Apply closes dialog (**K1**); failed Apply does not.

---

## Delivery order within Phase F

1. Serialize / deserialize helpers for collection / folder / request.  
2. Apply replace + undo + dirty + id preserve + collection file sync.  
3. Modal dialog: editor, Format, Find, confirm, error-without-close, discard prompt.  
4. Menu / command wiring (`CanExecute` = single selection).

---

## Acceptance criteria

- [ ] Edit as JSON enabled only for exactly one selected collection/folder/request.
- [ ] Buffer is internal camelCase JSON for that subtree only.
- [ ] Apply always confirms; success replaces subtree, one undo step, dirty, dialog closes.
- [ ] Apply failure shows error and **leaves dialog open** with buffer intact for further edits.
- [ ] Cancel/close with dirty buffer prompts Discard / Keep editing.
- [ ] Format + Find available in the dialog.
- [ ] Root id preserved on Apply (D1); collection rename syncs disk file (E1).
- [ ] LastResponse not carried onto replaced request VMs.
- [ ] Replay not implemented; no agent commits.

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

## Open follow-up (not blocking implement)

- **Q4 revisit:** D1 (keep root id) vs D2 (accept payload id). Implementing **D1** now; easy to switch later if you prefer D2.

---

## Next step

Decisions are recorded. Reply **approve Phase F** / **implement Phase F** when you want coding to start.
