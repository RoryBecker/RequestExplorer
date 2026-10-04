# Phase G — Investigation: fidelity gaps (plan)

**Status:** Decisions recorded (2026-10-05) — awaiting explicit **implement** instruction  
**Created:** 2026-10-05  
**Updated:** 2026-10-05 (user answers)  
**Parent backlog:** [`PLAN_IMPROVEMENTS_BACKLOG.md`](PLAN_IMPROVEMENTS_BACKLOG.md)  
**Repo policy:** Human owns all commits. Agent does not commit.

**This phase is investigation-only (L1).** Deliverable is `FIDELITY_GAPS.md`. No product fidelity fixes in G.

**Window-move lag (G2)** is **cancelled / deferred** for this phase (**F3** + **M2**). Item 17 remains in the backlog for a later pass.

**Replay mode** remains **parked (Phase Z)** and is **out of scope**.

---

## Scope (locked)

| # | Item | Status |
|---|------|--------|
| G1 | **Fidelity gap analysis** → `FIDELITY_GAPS.md` | **In scope** |
| G2 | Window-move lag root-cause | **Cancelled for Phase G** (defer) |

**Paths in the report (C4):**

1. Local save → reload  
2. Postman collection export  
3. Postman collection import (as collection + into folder)  
4. Internal Edit-as-JSON (brief sanity — same model)  
5. **Quick Export** accuracy notes (curl / etc.)  
6. **Backup restore** equivalence note (should match on-disk collections)

**Surfaces in the main table (B1):** request fields + collection/folder variables + names / ids / order.

**Postman-only features with no RE UI (D1):** list as N/A — no UI / Dropped on import (auth variants, non-raw body, scripts, descriptions, etc.).

**Out of scope:** implementing fidelity fixes, new auth/body modes, G2 instrumentation/fix, Replay (Z), Phase H refactors.

---

## Decisions (2026-10-05)

| Q | Code | Decision |
|---|------|----------|
| Q1 | **A1** | Code-trace + small ephemeral fixtures (spot-checks). |
| Q2 | **B1** | Request + collection/folder variables + names/ids/order. |
| Q3 | **C4** | Local + Postman + Edit-as-JSON sanity + **Quick Export** notes + **backup restore** note. |
| Q4 | **D1** | List Postman-only drops (even with no RE UI). |
| Q5 | **E1** | Deliverable = root **`FIDELITY_GAPS.md`**. |
| Q6 | **F3** | **Cancel / defer G2** (window-move lag) — not in this phase. |
| Q7 | **G1** | *(Recorded; N/A while G2 deferred.)* DEBUG probes if G2 is ever resumed. |
| Q8 | **H1** | *(Recorded; N/A while G2 deferred.)* Small fix OK if G2 resumes and finds a clear RE fault. |
| Q9 | **I1** | Ephemeral fixtures only — not committed. |
| Q10 | **J1** | Engineer-facing: table + code pointers + follow-up tickets. |
| Q11 | **K1** | End with suggested priority; you choose what to fix later. |
| Q12 | **L1** | **Analysis only** — no fidelity product fixes in G. *(Explicitly approved.)* |
| Q13 | **M2** | **G1 only** — skip move-lag. |

---

## Current state (code snapshot — 2026-10-05)

### Local model (what RequestExplorer can own)

| Surface | Model / UI today |
|---------|------------------|
| Collection | `Id`, `Name`, `Variables`, `Folders`, `Requests` |
| Folder | `Id`, `Name`, `Variables`, `Folders`, `Requests` |
| Request | `Id`, `Name`, `Verb`, `Address`, `Params`, `Headers`, `Authentication`, `Body` |
| Auth | **None / Bearer / Basic / ApiKey** (header or query) only |
| Body | **Raw text** + optional `ContentType` string (no form-data / urlencoded / file / graphql modes in model) |
| Key/value rows | `IsEnabled`, `Key`, `Value` (no description column) |
| Session-only | `LastResponse`, undo stack, dirty flags, tree expand/selection, filter text |

Persistence: `CollectionPersistenceService` — camelCase `System.Text.Json`, name-based collection files + order file.  
Internal Edit-as-JSON (Phase F): `SubtreeJsonService` (same internal model).  
Quick Export: `RequestExportService` (curl / HTTPie / etc. — not Collection JSON).  
Backups: `WorkspaceBackupService` zips collections (+ prefs in archive); in-app restore = collections only.

### Postman Collection v2.1 path

| Piece | Location |
|-------|----------|
| Models | `Services/Postman/PostmanCollectionModels.cs` |
| Map in/out | `Services/Postman/PostmanCollectionMapper.cs` |

**Starting hypotheses (verify in G1):**

| Area | Likely status |
|------|----------------|
| Auth: bearer / basic / apikey | Mapped both ways |
| Auth: oauth1/2, digest, awsv4, ntlm, hawk, edgegrid, collection/folder auth | No UI + import likely → None |
| Body: `raw` (+ json/text language → ContentType) | Partial map |
| Body: `urlencoded`, `formdata`, `file`, `graphql` | Import ignores; export never emits |
| Scripts / `event` | Not in models — dropped on import |
| Descriptions | Not in models |
| `protocolProfileBehavior`, certificates, examples | Not in models |
| Disabled query/header | `IsEnabled` ↔ `disabled` |
| Folder / collection variables | Mapped |
| Content-Type header vs body ContentType | Needs careful row |
| Params ↔ URL query sync | Needs careful row |

---

## G1 — Fidelity gap analysis (locked method)

1. **Inventory** editable request / folder / collection fields (names, ids, order, variables).  
2. **Code-trace** local save/load, Postman export/import, Edit-as-JSON, Quick Export emitters, backup restore path.  
3. **Ephemeral fixture** spot-checks for high-risk Partial/Dropped candidates (**I1** — not committed).  
4. Write **`FIDELITY_GAPS.md`** (**E1**, **J1**): status table + code loci + suggested priority (**K1**).  
5. **Stop** — no product fixes (**L1**).

### Table shape

| Field / surface | In RE UI? | Local save/load | Postman export | Postman import | Quick Export | Backup restore | Severity | Notes / code locus |
|-----------------|-----------|-----------------|----------------|----------------|--------------|----------------|----------|--------------------|

(Columns may be split across subsections if the wide table is unwieldy; every B1 field must still get Local + Postman export/import. Quick Export and Backup can be subsection tables where “n/a for this field” is common.)

**Path status codes:** Full / Partial / Dropped / N/A — no UI / UI-only / Unsupported export  

**Severity:** S0 (local loss) · S1 (Postman loss of RE-editable data) · S2 (lossy edge) · S3 (Postman-only, no UI) · Info (by design)

---

## G2 — Window-move lag (**cancelled for this phase**)

Deferred per **F3** + **M2**. Backlog Item 17 / former Q28 remains available later. Recorded preferences if resumed: DEBUG probes (**G1**), small fix OK (**H1**).

---

## Delivery order within Phase G

1. Field inventory from editors + models.  
2. Trace local + Postman + Edit-as-JSON.  
3. Trace Quick Export + backup restore (C4).  
4. Ephemeral fixture spot-checks.  
5. Finalise `FIDELITY_GAPS.md` with suggested follow-up priority.  
6. Update backlog pointer; stop for your prioritisation.

---

## Acceptance criteria

- [ ] `FIDELITY_GAPS.md` covers B1 surfaces with Local / Postman export / Postman import status.  
- [ ] Postman-only drops listed (D1), including auth/body/scripts/descriptions at minimum.  
- [ ] Quick Export notes and backup restore equivalence note included (C4).  
- [ ] Brief Edit-as-JSON sanity (same internal model).  
- [ ] Suggested fix priority section (K1).  
- [ ] No product behaviour changes (L1); G2 not done; no agent commits; Replay not implemented.

---

## Questions (recorded — chosen path marked ✓)

### Q1 — Fidelity depth

| Code | Option | |
|------|--------|-|
| **A1** | **Code-trace + small fixtures** (recommended): inventory, mapper/persistence trace, a few hand-built Postman JSON spot-checks. | ✓ **Chosen** |
| **A2** | Code-trace only (faster; fixtures optional later). | |
| **A3** | Deep: A1 + checked-in sample Postman collections + optional automated round-trip tests in the test project (larger G). | |

### Q2 — Which RE surfaces belong in the main table?

| Code | Option | |
|------|--------|-|
| **B1** | **Request + collection/folder variables + names/ids/order** (recommended). | ✓ **Chosen** |
| **B2** | Request fields only. | |
| **B3** | B1 + session-only rows (LastResponse, dirty, expand) marked UI-only. | |

### Q3 — Extra paths beyond local + Postman collection?

| Code | Option | |
|------|--------|-|
| **C1** | **Local + Postman import/export only** (+ brief Edit-as-JSON sanity) — recommended. | |
| **C2** | C1 + Quick Export accuracy notes (curl/etc.). | |
| **C3** | C1 + backup restore equivalence note. | |
| **C4** | All of C2 + C3. | ✓ **Chosen** |

### Q4 — How to treat Postman features with no RE UI?

| Code | Option | |
|------|--------|-|
| **D1** | **List them as N/A — no UI / Dropped on import** (recommended — shows the real gap surface). | ✓ **Chosen** |
| **D2** | Only analyze fields that exist in the RE editor (smaller report). | |

### Q5 — Deliverable location

| Code | Option | |
|------|--------|-|
| **E1** | New root file **`FIDELITY_GAPS.md`** (recommended; matches backlog). | ✓ **Chosen** |
| **E2** | Append a large section to `PLAN_IMPROVEMENTS_BACKLOG.md` only. | |
| **E3** | Both: full report in `FIDELITY_GAPS.md` + short summary in the backlog. | |

### Q6 — Window-move lag packaging

| Code | Option | |
|------|--------|-|
| **F1** | **Include G2 in Phase G**; lag note as a section inside `FIDELITY_GAPS.md`. | |
| **F2** | Include G2; separate `WINDOW_MOVE_LAG.md`. | |
| **F3** | **Defer G2** entirely (fidelity only this phase). | ✓ **Chosen** (user: “cancel”) |

### Q7 — Instrumentation for G2

| Code | Option | |
|------|--------|-|
| **G1** | **Temporary DEBUG-only / easily removed logs** during investigation; strip before you commit (or leave a commented hook). | ✓ **Chosen** *(N/A this phase — G2 deferred)* |
| **G2** | No code changes even for probes — observation + code reading only. | |
| **G3** | Keep a permanent low-cost diagnostic toggle in Settings (heavier; usually overkill for G). | |

### Q8 — If G2 finds a clear RE fault

| Code | Option | |
|------|--------|-|
| **H1** | **Fix in Phase G** if the change is small (e.g. stop redundant work on move). | ✓ **Chosen** *(N/A this phase — G2 deferred)* |
| **H2** | Report only; fix in a later phase even if trivial. | |

### Q9 — Fixture / sample policy

| Code | Option | |
|------|--------|-|
| **I1** | **Ephemeral fixtures** during investigation (not committed) — recommended for A1. | ✓ **Chosen** |
| **I2** | Commit a small `testdata/postman/` set of Postman JSON samples used in the report. | |
| **I3** | No fixtures; code-trace only (aligns with A2). | |

### Q10 — Report tone / audience

| Code | Option | |
|------|--------|-|
| **J1** | Engineer-facing: table + code pointers + follow-up tickets (recommended). | ✓ **Chosen** |
| **J2** | Shorter executive summary + appendix table. | |

### Q11 — After the report, who prioritises fixes?

| Code | Option | |
|------|--------|-|
| **K1** | Report ends with **suggested priority**; you pick what becomes Phase G+ / H / etc. (recommended). | ✓ **Chosen** |
| **K2** | Report only; no suggested priority order. | |

### Q12 — Scope creep: implement fixes in Phase G?

| Code | Option | |
|------|--------|-|
| **L1** | **Analysis only** — no fidelity product fixes in G (recommended; matches backlog). | ✓ **Chosen** *(explicitly approved)* |
| **L2** | Analysis + fix any **S0** (local save/load loss) found. | |
| **L3** | Analysis + fix S0 and obvious **S1** Postman drops for fields RE already edits. | |

### Q13 — Full Phase G package

| Code | Option | |
|------|--------|-|
| **M1** | Do **G1 + G2** as scoped by your other answers (recommended if F1/F2). | |
| **M2** | **G1 only** (fidelity); skip move-lag. | ✓ **Chosen** |
| **M3** | **G2 only** (move-lag); skip fidelity (unusual). | |

---

## Answer key (summary)

| Q | Choice | Notes |
|---|--------|-------|
| Q1 | **A1** | Code-trace + ephemeral fixtures |
| Q2 | **B1** | Request + containers + names/ids/order |
| Q3 | **C4** | + Quick Export + backup restore |
| Q4 | **D1** | List Postman-only drops |
| Q5 | **E1** | `FIDELITY_GAPS.md` |
| Q6 | **F3** | Cancel / defer window-move lag |
| Q7 | **G1** | N/A this phase (G2 deferred) |
| Q8 | **H1** | N/A this phase (G2 deferred) |
| Q9 | **I1** | Ephemeral fixtures |
| Q10 | **J1** | Engineer-facing report |
| Q11 | **K1** | Suggest priority |
| Q12 | **L1** | Analysis only *(explicitly approved)* |
| Q13 | **M2** | G1 only |

---

## Next step

Decisions are recorded (including **L1**). Reply **implement Phase G** (or **approve Phase G** / start investigation) when you want the fidelity report written.
