# Phase C — Variables workflow (plan)

**Status:** Approved (2026-10-05)  
**Created:** 2026-10-05  
**Updated:** 2026-10-05 (approved)  
**Parent backlog:** [`PLAN_IMPROVEMENTS_BACKLOG.md`](PLAN_IMPROVEMENTS_BACKLOG.md)  
**Repo policy:** Human owns all commits. Agent does not commit.

---

## Scope (from backlog Phase C)

| # | Item | Backlog ref |
|---|------|-------------|
| C1 | Right-click **promote / move** params, headers, and variables to **named** up/down targets | Item 1 |
| C2 | Show **read-only inherited** variables in the Variables UI | Item 3 + Decision #2 |
| C3 | Tree **glyph** on nodes that define variables + **hover** listing them | Item 19 |

**Out of scope for Phase C:** request-scoped variables (none today), Auth/Body promote, hide-toggle for inherited section, navigate-to-source on inherited row click, Replay, backups (E), modify-as-JSON (F).

---

## Decisions (2026-10-05)

| # | Topic | Decision |
|---|--------|----------|
| Q1 | Variable name on promote | **(a)** Use the row’s **Key** as the variable name (no prompt). |
| Q2 | Effect on source row | **Move only.** Promoting a param, header, or variable **never changes** its Key or Value — it only **relocates** the row between appropriate nodes/lists. No `{{…}}` rewrite. Source row is **removed** from the origin list; an equivalent row is **inserted** on the target (before spare). |
| Q3 | Surfaces (v1) | **(a)** Params + Headers + Variables only. |
| Q4 | Key collision on target | **(b)** Confirm dialog before overwrite; dialog **shows value comparison** for the key (existing vs incoming). |
| Q5 | Missing value | Empty **Value** is fine — promote/move anyway. *(Key still required: blank-key / spare rows stay ineligible.)* |
| Q6 | Move down targets | **(a)** Direct child folders only. |
| Q7 | Rewrite `{{name}}` refs | **(a)** No rewrite. |
| Q8 | Inherited placement | **(b)** **Above** Local. |
| Q9 | Shadowing | **(a)** Still show inherited row, muted + “overridden” badge. |
| Q10 | Inherited order | **(c)** Farthest ancestor first (collection → … → parent). |
| Q10b | Disabled ancestors | *(Unanswered — default **(a)** only enabled, matches resolver.)* |
| Q11 | Tree glyph | **(b)** Only if at least one **enabled** keyed variable. |
| Q12 | Tooltip secrets | **(a)** Show values as stored; truncate long ones. |
| Q13 | Tooltip limits | **(a)** Up to 20 lines; values truncated at 64 chars. |
| Q14 | Build order | *(Unanswered — default **(a)** C3 → C2 → C1.)* |
| Q15 | Drop / defer | *(Unanswered — nothing dropped.)* |

Also locked from backlog:

| Topic | Decision |
|-------|----------|
| Move UX | Right-click menus with **concrete named targets** |
| Inherited vars | Show **read-only** inherited when possible |
| Undo | Promote/move undoable (`CompositeUndoAction` as needed) |
| Dirty | Changes mark dirty for Ctrl+S |
| Commits | Human only |

---

## Current state (code snapshot)

### Resolution

- `VariableResolver.BuildScope` walks `request.Parent` chain; **nearest ancestor wins**.
- Only **enabled** variables with non-blank keys participate.
- Tokens are `{{name}}`. Auth/URL/params/headers/body already participate in diagnostics / resolve.

### Storage / UI

- Variables live only on **containers** (collection / folder). Requests have Params/Headers, not Variables.
- Collection / folder editors: single editable Variables `DataGrid` (local + spare).
- Phase D grid clipboard adds Copy/Paste to context menus — promote/move must **compose** with that.
- Tree: Segoe MDL2 type icons; no variable glyph yet.
- Undo covers insert/remove/set for variables and key/value rows.

### Gaps Phase C must close

1. No promote/move-to-scope commands.
2. Variables UI does not show ancestor contributions or shadowing.
3. Tree does not advertise “this node defines variables.”

---

## C1 — Promote / move to named targets

### Intent

**Relocate** a key/value row between Params, Headers, and Variables scopes via named menu targets — same Key, Value, and enabled state; no renaming and no value rewriting.

### Proposed UX

**Params / Headers row** (request editor), row with non-blank Key (Value may be empty):

- Submenu **Move to variable on…** (wording TBD; “Promote” OK if it means relocate)
  - One item per ancestor container: `folder 'Auth'`, `collection 'API'`.
  - Action:
    1. If target already has same key (case-insensitive) → **confirm dialog** with existing value vs incoming value; Cancel aborts; OK overwrites target variable and removes source row.
    2. Else remove source param/header row; insert variable on target (before spare) with same Key/Value/IsEnabled.
    3. Dirty + undoable composite (remove source + insert/update target).

**Variables row** (collection / folder), local editable row with non-blank Key:

- **Move up to…** — each ancestor container.
- **Move down to…** — each **direct child folder** only (Q6a). Not requests.
- Same collision confirm + value comparison (Q4b).
- Remove from source; insert on target; undoable.

**Ineligible:** spare/blank-key rows; inherited rows (C2).

### Proposed implementation sketch

- Context-menu helper (extend clipboard attach or sibling) builds dynamic Move/Promote items on `ContextMenuOpening`.
- Confirm dialog: title e.g. “Variable already exists”; body shows Key, **Current value**, **New value**, Overwrite / Cancel.
- VM/service methods: enumerate ancestor containers; enumerate direct child folders; `MoveKeyValueToVariable` / `MoveVariableToContainer` as composites.
- Param moves must keep URL↔Params sync consistent after row removal (existing spare/normalize paths).

### Risks

- Collision confirm must be clear when values differ vs when they are identical.
- Moving a param out may change the request URL query string (existing sync) — expected if Key/Value leave Params.
- Menu label “Promote” vs “Move” — prefer **Move** language to match Q2.

---

## C2 — Read-only inherited variables

### Intent

Show ancestor contributions above local variables; call out overrides.

### Proposed UX

1. **Inherited** (above Local — Q8b) — read-only grid/list: Key, Value, Source; muted “overridden” when local defines same key (Q9a).
2. **Local** — existing editable grid + spare.

**Order (Q10c):** farthest ancestor first (collection → … → immediate parent); keys within a source group A–Z (sensible default).

**Include:** enabled ancestor vars only (Q10b default). Not persisted as local.

**Refresh** on selection change, local/ancestor variable edits, undo/redo.

### Risks

- Deep trees → long lists; group-by-source headers if needed later.
- Read-only must be obvious (no On toggle / remove / paste into inherited).

---

## C3 — Tree glyph + hover

### Intent

See which containers define variables; hover to inspect.

### Proposed UX

- Glyph when container has ≥1 **enabled** keyed variable (Q11b).
- Tooltip: up to **20** lines, `name=value`, values truncated at **64** chars (Q12a, Q13a).
- Live update on variable changes / undo / import / paste.
- Requests: no glyph.

---

## Delivery order within Phase C

Default: **C3 → C2 → C1** (Q14 unanswered). Override if you prefer actions first.

---

## Acceptance criteria

- [x] Param/header/variable row can be **moved** to a named legal target; Key/Value/Enabled unchanged; source row gone from origin.
- [x] Collision shows confirm with **value comparison**; Cancel leaves both sides unchanged.
- [x] Empty Value allowed; blank-key rows cannot move.
- [x] Move down lists direct child folders only; no request targets; no `{{…}}` rewrite.
- [x] Inherited section **above** Local; farthest-first; overridden rows muted + badge; not saved as local.
- [x] Tree glyph when enabled keyed vars exist; tooltip limits per Q12–Q13.
- [x] All moves undoable; dirty marked; no agent commits.

---

## Open questions (answered)

| Q | Answer |
|---|--------|
| Q1 | **a** — Key as variable name |
| Q2 | **Move only** — never change name/value; relocate only |
| Q3 | **a** — Params / Headers / Variables |
| Q4 | **b** — Confirm + value comparison |
| Q5 | Empty value OK; promote anyway (Key still required) |
| Q6 | **a** — Direct child folders |
| Q7 | **a** — No reference rewrite |
| Q8 | **b** — Inherited above Local |
| Q9 | **a** — Show overridden, muted + badge |
| Q10 | **c** — Farthest ancestor first |
| Q11 | **b** — Glyph if any enabled keyed var |
| Q12 | **a** — Show values; truncate |
| Q13 | **a** — 20 lines / 64 chars |

Still optional: **Q10b**, **Q14**, **Q15** (defaults above).

---

## Shipped (2026-10-05)

- **C3:** Tree glyph (`E81E`) + tooltip on collection/folder when ≥1 enabled keyed variable.
- **C2:** Inherited grid above Local on collection/folder editors; overridden rows muted + “Overridden”; farthest-ancestor-first.
- **C1:** Context menus Move to variable on… / Move up to… / Move down to…; overwrite confirm with value comparison; undoable composites via `VariableScopeMove`.

Human owns commits.
