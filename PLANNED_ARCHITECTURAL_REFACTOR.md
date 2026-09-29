# RequestExplorer — Architecture Refactor Phases

**Status:** Planned (not started)  
**Created:** 2026-09-29  
**Companion to:** [`PLAN_COMPLETED.md`](PLAN_COMPLETED.md) (original feature plan — Phases 0–6 complete)  


**Repo policy:** Do **not** auto-commit. User commits only after explicitly approving each phase.  
**Verification policy:** After **every** phase below, run the regression checklist and confirm behaviour is **unaltered** before moving on.

---

## Purpose

Capture architectural antipatterns observed in the current codebase and turn the highest-value remediations into **future phases**. These phases are structural only: they must not change product behaviour, UX flows, persistence shape, or undo semantics unless a phase explicitly says otherwise (none do today).

---

## Current-state findings (snapshot)

### Top offenders

| Area | Approx. size / role | Concern |
|------|---------------------|---------|
| `ViewModels/MainViewModel.cs` | ~2.1k lines — app shell | **God object.** Owns workspace load/save, HTTP send/cancel, Postman import/export, clipboard copy/paste, tree insert/remove/move, undo orchestration + reveal, Quick Export pipeline, **and** direct WPF (`MessageBox`, file dialogs, `Clipboard`, `Keyboard`, `Dispatcher`, `Application.Current`). |
| `ViewModels/RequestNodeViewModel.cs` | ~800 lines — request editor | Fat presentation model: auth/body/params/headers sync, spare-row UX, undo recording, URL↔query bidirectional sync. Overlaps key-value/undo patterns in `ContainerNodeViewModel`. |
| `MainWindow.xaml.cs` | ~490 lines — shell code-behind | Acceptable as UI orchestration (prefs layout, tree focus, ad-hoc menus), but tightly coupled to the large `MainViewModel` surface. Not a god object by itself. |
| `TreeDragDropController.cs` | ~450 lines — tree DnD | Good extraction from the window; still **hard-wired** to `MainViewModel` via `TryMoveNode`. Mutation rules live on the shell VM. |

### Coupling hotspots

- **Persistence:** `LoadWorkspace` / `Save` / `SaveAllCollections` + `CollectionTreeMapper.SyncModelFromView` inside `MainViewModel`.
- **HTTP:** `SendRequestAsync` → diagnosis → `VariableResolver.Resolve` → `HttpRequestExecutor`.
- **Export (two paths):** Quick Export preview (`RequestExportService`) and file Export/Import (`PostmanCollectionMapper` + dialogs).
- **Undo:** `MainViewModel` implements both `IUndoContext` and `IUndoRecorder`; reveal logic ties undo to request **tab index** (`EditorSurface`, `SelectedRequestTabIndex`).
- **Tree + DnD:** `TreeDragDropController` → `MainViewModel.TryMoveNode` / insert-remove helpers.
- **WPF in ViewModel:** `CanUndo`/`CanRedo` check `Keyboard.FocusedElement`; prompts/dialogs constructed in the VM.

### Duplication / drift risk

- Query-string build/parse appears in `RequestNodeViewModel`, `RequestExportService`, and `PostmanCollectionMapper` (token-preserving escape already shared via `VariableResolver`).
- `DuplicateRequest` hand-copies fields instead of using `TreeNodeCloneService`.
- Import / paste / duplicate share repeated “add to model + add child VM + wire undo” branches.
- Editable key-value spare-row + undo paths are near-duplicated between params/headers and collection/folder variables.

### Patterns that are fine (do not “fix” for their own sake)

- Thin models under `Models/`.
- Focused services: `CollectionPersistenceService`, `CollectionTreeMapper`, undo action types, `HttpRequestExecutor`.
- Large but cohesive mappers/exporters: `PostmanCollectionMapper`, `RequestExportService`, `VariableResolver` / `VariableDiagnostics`.
- Thin editor view code-behind (`NameTitleBox` exposure only).

---

## Cross-cutting rules for all phases

1. **Behaviour freeze.** No intentional product changes. If a behaviour change is unavoidable, stop and agree it explicitly before continuing.
2. **Manual regression after each phase** (see checklist below). Prefer the same scenarios used while building features (tree CRUD, send, undo, import/export, Quick Export, prefs).
3. **No drive-by refactors** outside the phase scope.
4. **Do not auto-commit.** User verifies, then commits.
5. Prefer **extract + delegate** over rewrite. Keep public UX commands and bindings stable where possible.

---

## Regression checklist (run after every phase)

Mark each item after manual verification:

### Workspace & persistence
- [ ] Demo/collections load on startup
- [ ] Edit request → dirty `*` on request and ancestors → **Ctrl+S** clears dirty and persists
- [ ] Save All works with multiple dirty collections
- [ ] Close with unsaved changes: Yes / No / Cancel (Escape = Cancel) behave as today

### Tree
- [ ] New Collection / Folder / Request; rename (F2, title commit, double-click); delete with Yes/No (Escape = No)
- [ ] Copy / paste into folder or collection; paste at root wrapping rules unchanged
- [ ] Drag-drop reorder / into folder / root indicators; Escape cancels drag
- [ ] Right-click selects node under cursor; context menus still work; Escape dismisses menus

### Request editor & send
- [ ] Verb / URL / params / headers / body / auth edit and dirty tracking
- [ ] URL ↔ params sync; `{{var}}` remains literal in address bar (not `%7B%7B…`)
- [ ] Collection/folder variables grid; nearest-ancestor resolution on Send
- [ ] Send blocked with diagnosis when variables missing; succeeds when resolved
- [ ] Cancel in-flight send still works
- [ ] Response status / body / headers / cookies / timing unchanged

### Undo / redo
- [ ] Ctrl+Z / Ctrl+Y and toolbar history menus
- [ ] Reveal-first behaviour for Params / Headers / Body still requires second undo to apply when not on that surface
- [ ] Variable edits on collection/folder undo/redo and reselect the node
- [ ] Tree structural undo (add/delete/move/paste) still inverts correctly

### Quick Export & prefs
- [ ] Panel toggle show/hide; splitter resize remembered
- [ ] Format selection remembered between sessions
- [ ] Wrap toggle per format; cURL (Windows) defaults to wrap; others default off; remembered between sessions
- [ ] Missing-variable diagnosis text appears under export when applicable
- [ ] cURL vs cURL (Windows) line-continuation difference unchanged

### Postman interchange
- [ ] Export collection/folder/request to Postman v2.1
- [ ] Import into collection/folder (including variables where applicable)

### Escape behaviour
- [ ] Escape closes context menus, chooses No on Yes/No, Cancel on Yes/No/Cancel, cancels drag

**Phase exit criterion:** All applicable boxes checked; no intentional behaviour deltas; user approves commit.

---

## Future phases

### Phase A — Split `MainViewModel` into coordinators

**Goal:** Shrink the shell VM to bindings + command facades. Move cohesive logic into focused types, e.g.:

- `WorkspacePersistenceCoordinator` — load / save / dirty / seed
- `TreeDocumentService` — insert / remove / move / attach helpers
- `RequestSendCoordinator` — diagnose / resolve / send / cancel
- `ExportImportCoordinator` — Postman file IO + Quick Export refresh orchestration

**In scope:** Extract + delegate; keep command names and observable properties stable where practical.  
**Out of scope:** New features; changing undo semantics; DI container (unless trivial constructor wiring).

**Done when:** `MainViewModel` is substantially thinner; behaviour checklist passes.

---

### Phase B — Tree mutations behind a service (DnD decoupling)

**Goal:** Own `InsertNodeCore` / `RemoveNodeCore` / `TryMoveNode` (and related model attach/detach) on a `TreeDocumentService` (or similar). Implement `IUndoContext` there or via a thin adapter. Point `TreeDragDropController` at an interface instead of `MainViewModel`.

**In scope:** Move/tree API extraction; controller depends on abstraction.  
**Out of scope:** Changing drop placement rules or visual indicators.

**Done when:** DnD and structural undo still match pre-phase behaviour; checklist passes.

---

### Phase C — Dialog / file-picker abstraction

**Goal:** Remove direct `MessageBox`, `OpenFileDialog`, `SaveFileDialog`, and owned-window dialog construction from ViewModels. Introduce something like `IUserInteraction` (WPF implementation in the view/app layer).

**In scope:** Name prompts, delete confirm, unsaved-close, import/export pickers, diagnosis report host, about.  
**Out of scope:** Redesigning dialog copy or layout.

**Done when:** Same prompts and results; Escape/Yes/No/Cancel semantics unchanged; checklist passes.

---

### Phase D — Shared editable key-value list helper

**Goal:** Deduplicate spare-row, field apply, remove/insert, and undo recording shared by:

- Request Params / Headers (`RequestNodeViewModel`)
- Collection / Folder Variables (`ContainerNodeViewModel`)

**In scope:** Helper or small base/mixin; keep bindings and undo descriptions equivalent.  
**Out of scope:** Changing grid UX or enabling request-level variables.

**Done when:** Params/headers/variables editing + undo behave identically; checklist passes.

---

### Phase E — Centralize query-string build/parse

**Goal:** Single helper (e.g. extend `VariableResolver` or add `RequestUrlHelper`) for build/parse used by:

- `RequestNodeViewModel` (address bar sync)
- `RequestExportService`
- `PostmanCollectionMapper` (as applicable)

Preserve `{{token}}` non-encoding behaviour.

**Done when:** URL bar, exports, and Postman raw URLs match prior behaviour; checklist passes.

---

### Phase F — Unify cloning and tree attach paths

**Goal:**

1. `DuplicateRequest` uses `TreeNodeCloneService` (then rename) instead of manual field copy.
2. Collapse repeated import/paste/duplicate “attach to parent + wire undo” branches into one attach/factory API.

**Done when:** Duplicate/copy/paste/import produce the same tree shapes and undo stacks; checklist passes.

---

### Phase G — Relocate undo reveal / editor navigation

**Goal:** Keep core undo tree-focused; move “show Params/Headers/Body tab before apply” policy behind a small `IEditorNavigation` (or view-owned service) so `MainViewModel` is not the sole owner of tab-index reveal rules.

**Done when:** Reveal-first undo/redo for request surfaces is unchanged; checklist passes.

---

### Phase H — Optional follow-ups (lower priority)

Only if needed for testing or further growth:

- Extract URL↔params sync from `RequestNodeViewModel` into a dedicated helper for unit tests.
- Light constructor DI / composition root in `App` if multiple hosts or automated tests appear.
- Split `RequestExportService` by format only if the format matrix becomes painful.

---

## Suggested order

| Order | Phase | Rationale |
|------:|-------|-----------|
| 1 | **A** | Unlocks everything else; biggest readability win |
| 2 | **B** | Natural continuation of A’s tree extraction |
| 3 | **C** | Improves testability of the shell without touching domain rules |
| 4 | **D** | Stops key-value/undo drift |
| 5 | **E** | Small, localized, reduces encoding bugs |
| 6 | **F** | Cleanup once tree attach API exists (after A/B) |
| 7 | **G** | Polish undo/UI boundary |
| 8 | **H** | As needed |

Phases may be combined only if the combined diff stays reviewable and the full checklist is run once at the end of the combined unit of work.

---

## Tracking

| Phase | Status | Verified (checklist) | Commit (user) |
|-------|--------|----------------------|----------------|
| A — Split MainViewModel | Not started | ☐ | |
| B — Tree service / DnD | Not started | ☐ | |
| C — Dialog abstraction | Not started | ☐ | |
| D — Key-value helper | Not started | ☐ | |
| E — Query-string helper | Not started | ☐ | |
| F — Clone / attach unify | Not started | ☐ | |
| G — Undo reveal navigation | Not started | ☐ | |
| H — Optional follow-ups | Not started | ☐ | |

---

## Notes

- This document does **not** supersede the completed feature plan in `PLAN_COMPLETED.md`; it is the **planned** post-feature structural roadmap (not yet started).
- Prefer finishing user-facing features first; start Phase A when churn on `MainViewModel` becomes the bottleneck.
