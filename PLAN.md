# RequestExplorer — Multiphase Plan

**Status:** Phase 1 implemented — awaiting your verification & commit  
**Created:** 2026-09-29  
**Repo policy:** Do **not** auto-commit. User commits only after explicitly approving each phase as complete.

---

## Product summary

A single-window Windows desktop app (C# / WPF) named **RequestExplorer** for composing and sending HTTP requests, organized in a Collection → Folder → Request tree (Explorer-style left pane). The right pane shows an editable request editor with a Send action and a response viewer (status, body, duration, headers, cookies, timing). Edits are in-place; dirty state is visible on tree nodes via `*`; **Ctrl+S** persists the current request.

---

## Locked preferences (2026-09-29)

User selections (omitted **K** → ★ default **K1**):

| Label | Meaning |
|-------|---------|
| **A2** | Workspace with many collections as top-level tree roots |
| **B2** | Basic New / Rename / Delete from Phase 1 (context menu or toolbar) |
| **C1** | Drag-and-drop not in v1 |
| **D3** | Product name: **RequestExplorer** |
| **E1** | One JSON file per collection |
| **F1** | Persist only via Ctrl+S (and optional menu Save) |
| **G★** | Custom (see below) — not a stock G1–G4 pick |
| **H1** | Fixed verb dropdown: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS |
| **I2** | Auth: None + Bearer + Basic + **API Key** (header or query) |
| **J1** | Body: raw text / JSON only for Phases 3–5 |
| **K1** | TLS: system defaults only (default; not stated) |
| **L2** | Fixed timeout + **Cancel** while sending |
| **M4** | Response: status + duration + body + headers + cookies + timing breakdown |
| **N1** | Large bodies: load fully; warn over soft size (~5 MB) |
| **O1** | Latest response only (replaced each Send) |
| **P4** | Unpackaged Win10/11 now; revisit packaging after Phase 6 |
| **R1** | Manual UI verification per phase only |
| **S2** | Seeded demo collection (e.g. httpbin.org) |

### Q7 / dirty behavior (custom — locked)

- Every request stays **in memory** whether dirty or clean; switching selection does **not** discard or force-save.
- A dirty request shows an **`*`** on its tree node.
- Ancestor nodes (folders / collections) that **contain** any dirty descendant also show **`*`** so dirtiness is visible up the tree.
- App-exit prompt for unsaved dirties: TBD lightly in Phase 4 (recommended: warn if any dirty remain); not required to block Phase 0–3.

---

## Assumptions (aligned to locked preferences)

| # | Assumption | Rationale |
|---|------------|-----------|
| Asm1 | **.NET 8** (or latest LTS on the machine) + **WPF** desktop project | Modern, supported |
| Asm2 | **MVVM** (CommunityToolkit.Mvvm or similar) | Tree + editor + dirty/save |
| Asm3 | **Persistence = local JSON**; **one file per collection** (**E1**) | Portable, simple |
| Asm4 | **Collection** roots contain folders and requests | Matches model |
| Asm5 | **Multi-collection workspace** as top-level tree roots (**A2**) | Locked |
| Asm6 | In-situ editing; Ctrl+S writes collection file(s) (**F1**) | Locked |
| Asm7 | Dirty tracking in memory; **`*`** on dirty request and on ancestors that contain dirty items (**G★**) | Locked |
| Asm8 | **Send** uses current in-memory (possibly dirty) values | Postman-like |
| Asm9 | HTTP via **`HttpClient`** | Built-in |
| Asm10 | Auth v1: **None, Bearer, Basic, API Key** (**I2**) | Locked |
| Asm11 | Body v1: **raw text/JSON** (**J1**) | Locked |
| Asm12 | Params = query string key/value pairs | Common |
| Asm13 | Headers = key/value; empty/disabled ignored on send | Postman-like |
| Asm14 | Response: status, duration, body, **headers, cookies, timing** (**M4**); latest only (**O1**); soft size warn (**N1**) | Locked |
| Asm15 | Folder/collection selected → RHS placeholder | Spec |
| Asm16 | No cloud sync / environments / scripting in v1 | Focus |
| Asm17 | English UI; simple theme | Defer polish |
| Asm18 | Product / solution name: **`RequestExplorer`** (**D3**) | Locked |
| Asm19 | Tree CRUD New/Rename/Delete from Phase 1 (**B2**); no DnD in v1 (**C1**) | Locked |
| Asm20 | Send: fixed timeout + Cancel (**L2**); packaging unpackaged for now (**P4**) | Locked |
| Asm21 | Manual testing only for now (**R1**); seeded httpbin demo (**S2**) | Locked |

---

## Open questions (reference — answered)

Each option has a **globally unique** label: **letter = question group**, **number = choice within that group**  
(Q1→`A*`, Q2→`B*`, … Q16→`P*`, Q17→`R*`, Q18→`S*` — letter **Q** skipped).

**Status:** answered 2026-09-29 — see **Locked preferences** above. Options kept below for audit.

### Scope & product

**Q1 — Tree roots** (`A*`)  
How are collections presented in the left tree?

- **A1** — Single collection open at a time (File → Open Collection)
- **A2** ★ ✓ — Workspace with many collections as top-level tree roots
- **A3** — Single hard-coded / last-opened collection only (no multi-collection UI in v1)

**Q2 — Tree create / delete / rename** (`B*`)  
When do CRUD actions appear?

- **B1** — Sample/seeded tree only until Phase 6
- **B2** ★ ✓ — Basic New / Rename / Delete from Phase 1 (context menu or toolbar)
- **B3** — Full CRUD as soon as the tree exists, including duplicate

**Q3 — Drag-and-drop** (`C*`)  
Reorder / move folders and requests in the tree?

- **C1** ★ ✓ — Not in v1 (later)
- **C2** — Required before Phase 6 is done
- **C3** — Nice-to-have in Phase 6 if time allows

**Q4 — Product name** (`D*`) — window title / assembly

- **D1** ★ — `Postman` (match folder name; local learning clone)
- **D2** — `HttpStudio`
- **D3** ✓ — `RequestExplorer`
- **D4** — Other (specify the name with your reply)

### Persistence & save semantics

**Q5 — Save target** (`E*`)  
How is data stored on disk?

- **E1** ★ ✓ — One JSON file per collection
- **E2** — Single workspace JSON file (all collections)
- **E3** — One JSON file per request (folders as directories)
- **E4** — Hybrid: collection index + per-request files

**Q6 — When to persist** (`F*`)  
How does saving work day-to-day?

- **F1** ★ ✓ — Explicit only: Ctrl+S (and optional menu Save)
- **F2** — Ctrl+S + offer “Save” when Send succeeds
- **F3** — Autosave shortly after edits (still show dirty briefly)
- **F4** — Ctrl+S + autosave collection structure on tree CRUD only

**Q7 — Dirty when changing selection** (`G*`)  
User selects another request while the current one is dirty?

- **G1** — Prompt: Save / Discard / Cancel
- **G2** ★ — Keep multiple dirty requests in memory; only prompt on app exit
- **G3** — Auto-save current before switching
- **G4** — Auto-discard dirty changes when leaving (no prompt)
- **G★** ✓ — **Custom (locked):** all requests remain in memory (dirty or not); tree nodes show `*` when dirty or when they contain a dirty descendant

### Request / HTTP feature depth

**Q8 — HTTP verbs** (`H*`)

- **H1** ★ ✓ — Fixed dropdown: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- **H2** — Fixed list above + editable custom verb
- **H3** — Free-text verb only

**Q9 — Auth strategies in early phases** (`I*`) — Phase 3–5

- **I1** ★ — None + Bearer + Basic only
- **I2** ✓ — I1 + API Key (header or query)
- **I3** — I2 + Windows/NTLM
- **I4** — Include OAuth2 early (larger scope)

**Q10 — Body modes in early phases** (`J*`)

- **J1** ★ ✓ — Raw text / JSON only for Phases 3–5
- **J2** — J1 + `x-www-form-urlencoded`
- **J3** — J2 + multipart form
- **J4** — J3 + binary/file upload

**Q11 — TLS / certificates** (`K*`)

- **K1** ★ ✓ — System defaults only (no ignore-SSL, no client certs) in v1 *(default; omitted in reply)*
- **K2** — Optional “skip certificate validation” (dev use)
- **K3** — K2 + client certificate picker
- **K4** — Defer all of this past Phase 6

**Q12 — Timeouts & cancel** (`L*`)

- **L1** — Fixed timeout only (e.g. 100s), no Cancel
- **L2** ★ ✓ — Fixed timeout + Cancel button while sending
- **L3** — User-configurable timeout + Cancel
- **L4** — Per-request timeout field + Cancel

### Response UX

**Q13 — Response panel contents** (`M*`)

- **M1** — Status + duration + body only
- **M2** ★ — M1 + response headers tab
- **M3** — M2 + cookies
- **M4** ✓ — M3 + timing breakdown (DNS/connect/TTFB/etc. as available)

**Q14 — Large response bodies** (`N*`)

- **N1** ★ ✓ — Load full body into memory; warn if over a soft size (e.g. 5 MB)
- **N2** — Hard truncate with “truncated” message
- **N3** — Stream to temp file; preview first N KB in UI
- **N4** — No special handling in v1

**Q15 — Response history** (`O*`)

- **O1** ★ ✓ — Latest response only (replaced on each Send)
- **O2** — Last N responses per request (e.g. N=10) in-session
- **O3** — Persist response history to disk
- **O4** — Global send history drawer (all requests)

### Tech / process

**Q16 — Packaging / OS** (`P*`)

- **P1** ★ — Unpackaged Win10/11 desktop (`dotnet run` / Framework-dependent)
- **P2** — Self-contained single-folder publish
- **P3** — MSIX / Store-style packaging later
- **P4** ✓ — P1 now; revisit packaging after Phase 6

**Q17 — Automated tests** (`R*`)

- **R1** ★ ✓ — Manual UI verification per phase only
- **R2** — xUnit for domain + HTTP from Phase 4+
- **R3** — xUnit from Phase 1 (models) onward
- **R4** — UI automation (e.g. FlaUI) — not in v1

**Q18 — Sample / demo data** (`S*`)

- **S1** — Empty tree; user builds everything
- **S2** ★ ✓ — Seeded demo collection (e.g. httpbin.org examples)
- **S3** — Seeded demo + “reset demo data” command
- **S4** — Load sample only on first run; then user file

### How to answer (historical)

```
A2 B2 C1 D3 E1 F1 H1 I2 J1 L2 M4 N1 O1 P4 R1 S2
```
Plus custom **G★** dirty/`*` rules. **K1** by default.

---

## Phase overview

| Phase | Name | Goal | Gate (approve before next) |
|-------|------|------|----------------------------|
| **0** | Foundations | Solution, folders, plan locked, empty shell window | Builds; opens blank main window |
| **1** | Domain + tree UI | Models + left Explorer tree; basic New/Rename/Delete (**B2**); seeded demo (**S2**) | Tree shows sample hierarchy; selection raises events |
| **2** | Selection → RHS shell | RHS layout: request editor chrome + empty response area; folder → blank | Selecting request/folder switches RHS correctly |
| **3** | Request editor (edit in place) | Verb, URL, Params, Headers, Auth (**I2**), Body bound; dirty + `*` on node/ancestors (**G★**) | Edits mutate selection; dirty/`*` works |
| **4** | Save (Ctrl+S) | Persist to one JSON per collection (**E1**/**F1**); clear dirty/`*` | Ctrl+S writes storage; reload reflects data |
| **5** | Send + response | HttpClient send; Cancel (**L2**); response **M4** panel | Round-trip against real URL works |
| **6** | Polish | UX polish, README; packaging revisit (**P4**); no DnD (**C1**) | Usable RequestExplorer for daily smoke tests |

Each phase below has: **objectives**, **deliverables**, **out of scope**, **acceptance criteria**, and **approval checklist**. Implementation starts only after you approve the phase (and relevant questions).

---

## Phase 0 — Foundations

**Objectives**
- Create the WPF solution/project structure in this workspace.
- Wire a single `MainWindow` with a placeholder split layout (left/right).
- Keep this plan as the living roadmap (`PLAN.md`); update status as phases complete.

**Deliverables**
- `.sln` + WPF project (C#)
- `MainWindow` with `Grid`/`GridSplitter` left | right placeholders
- `.gitignore` suitable for .NET
- Optional: short `README.md` (how to build/run) — only if you want docs in Phase 0

**Out of scope**
- Domain models, HTTP, persistence, real tree data

**Acceptance criteria**
- [x] `dotnet build` succeeds
- [x] App runs and shows a single window with visible left/right split
- [x] No git commit by the agent

**Approval checklist (you)**
- [x] Confirm .NET version / project name — **net8.0-windows** / **RequestExplorer**
- [ ] Approve Phase 0 complete → you commit → greenlight Phase 1

**Phase 0 deliverables (done)**
- `RequestExplorer.sln` + `RequestExplorer/RequestExplorer.csproj` (`net8.0-windows`)
- `MainWindow` with left | splitter | right placeholders
- `.gitignore`, `README.md`
- Product title: **RequestExplorer**

---

## Phase 1 — Domain model + left tree

**Objectives**
- Define `Collection`, `Folder`, `Request` (and shared tree-node abstraction).
- Bind a `TreeView` (or custom tree) on the left to a sample in-memory hierarchy.
- Selection of folder vs request is distinguishable.

**Deliverables**
- Domain types (verb enum/string, params, headers, auth stub, body stub as needed for structure)
- ViewModels for tree nodes
- Sample seed data (see Q18)
- Left pane populated tree

**Out of scope**
- Full editor forms, Send, persistence

**Acceptance criteria**
- [x] Tree shows nested collections/folders/requests
- [x] Selecting nodes updates a debug/status binding or selected-item VM
- [x] Folder vs request selection is clear in code

**Phase 1 deliverables (done)**
- Domain: `CollectionDefinition`, `FolderDefinition`, `RequestDefinition`, auth/body/params/headers, `HttpVerb`
- MVVM tree nodes + `MainViewModel`; seeded **httpbin.org Demo** + **Sandbox** collections
- Left `TreeView` with toolbar/context menu New Collection / Folder / Request, Rename, Delete (**B2**)
- Status bar selection summary (`Collection ·` / `Folder ·` / `Request · VERB ·`)
- Dirty `*` presentation hooks on nodes (active in Phase 3)

---

## Phase 2 — RHS shell by selection

**Objectives**
- Right pane: upper **Request** region, lower **Response** region (splitter).
- Request selected → show request chrome (even if fields are stubs).
- Folder/collection selected → empty/placeholder.

**Deliverables**
- RHS layout XAML
- Content switching on selection
- Placeholder response panel

**Out of scope**
- Full two-way field editing, dirty/save, HTTP

**Acceptance criteria**
- [ ] Folder → blank/placeholder RHS
- [ ] Request → editor + response panels visible
- [ ] Vertical splitter between editor and response works

---

## Phase 3 — Request editor + dirty state

**Objectives**
- Editable Verb, Address, Params, Headers, Auth, Body bound to the selected request VM.
- Any change marks the request **dirty**; UX shows dirty (e.g. tab/title asterisk, tree glyph, or banner).

**Deliverables**
- Form sections (tabs or accordion — decide in implementation with your preference)
- Dirty tracking on the request VM
- Visual dirty indicator

**Out of scope**
- Persistence, Send

**Acceptance criteria**
- [ ] Editing fields updates the in-memory request
- [ ] Dirty appears after first change; cleared only by Save (Phase 4) or explicit discard (if we add it)
- [ ] Switching selection respects agreed dirty policy (Q6/Q7) — may stub “keep dirty in memory” until Phase 4

**Depends on answers:** Q6, Q7, Q8–Q10 (depth of auth/body UI)

---

## Phase 4 — Save (Ctrl+S)

**Objectives**
- Ctrl+S saves the currently edited request to the agreed store.
- Clear dirty after successful save.
- Optionally: warn when navigating away while dirty (per Q6/Q7).

**Deliverables**
- Persistence service (JSON schema documented briefly in plan or README)
- Key binding Ctrl+S on main window / editor
- Load on startup from last path or default sample path

**Out of scope**
- Cloud, multi-user, migration tooling

**Acceptance criteria**
- [ ] Edit → Ctrl+S → restart app → values restored
- [ ] Dirty cleared after save
- [ ] Failed save surfaces an error (message box or status)

**Depends on answers:** Q5, Q6, Q7

---

## Phase 5 — Send + response panel

**Objectives**
- Send button near URL bar runs the selected request (current dirty values).
- Show response status code, body, and elapsed time.
- Disable Send / show busy state while in flight; handle errors (DNS, timeout, non-success still show body when present).

**Deliverables**
- HTTP execution service
- Response VM bound to lower panel
- Timing via stopwatch around the send

**Out of scope**
- Collections runner, tests/assertions, mock servers

**Acceptance criteria**
- [ ] GET to a public URL returns status + body + duration
- [ ] POST with JSON body works for a test endpoint
- [ ] Auth None / Bearer / Basic applied as configured
- [ ] Query params and headers applied correctly

**Depends on answers:** Q9–Q12, Q13–Q15

---

## Phase 6 — Tree CRUD & polish (optional / stretch)

**Objectives**
- New / rename / delete collection, folder, request (if Q2 requires it).
- Minor UX: icons, empty states, error toasts, README.
- Revisit deferred items from questions.

**Deliverables**
- Context menus or toolbar commands
- Save collection structure after tree mutations
- README with build/run and file format notes

**Acceptance criteria**
- [ ] User can build a small collection from scratch and send requests
- [ ] App feels coherent enough for repeated local use

---

## Suggested execution rhythm

1. You review this plan → answer questions (or “defer with assumption X”).
2. You **approve Phase N**.
3. Agent implements Phase N only; updates this file’s status.
4. You verify acceptance criteria → **you commit**.
5. Repeat for N+1.

No phase starts without your go-ahead. No agent git commits unless you later change that rule.

---

## Immediate next step

Phase 1 is implemented. Please verify the tree, selection summary, and CRUD, **commit when satisfied**, then **approve Phase 2**.

```powershell
dotnet run --project RequestExplorer
```

---

## Decision log

| Date | Decision | Notes |
|------|----------|-------|
| 2026-09-29 | Plan created | Initial draft |
| 2026-09-29 | Preferences locked | `A2 B2 C1 D3 E1 F1 H1 I2 J1 L2 M4 N1 O1 P4 R1 S2` + **G★**; **K1** default. **RequestExplorer**. |
| 2026-09-29 | Phase 0 implemented | WPF `net8.0-windows` scaffold; split MainWindow. |
| 2026-09-29 | Phase 0 approved by user | Proceed to Phase 1 |
| 2026-09-29 | Phase 1 implemented | Domain + TreeView + seed data + B2 CRUD; no agent commit. |
| | | |

---

## Phase status tracker

| Phase | Status |
|-------|--------|
| 0 Foundations | **Done (approved)** |
| 1 Domain + tree | **Done (pending your commit)** |
| 2 RHS shell | Not started |
| 3 Editor + dirty | Not started |
| 4 Save Ctrl+S | Not started |
| 5 Send + response | Not started |
| 6 CRUD + polish | Not started |
