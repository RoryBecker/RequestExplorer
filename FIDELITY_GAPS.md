# RequestExplorer — Fidelity gaps report (Phase G)

**Status:** Analysis complete; **must-fix items implemented** (2026-10-05)  
**Plan:** [`PLAN_PHASE_G.md`](PLAN_PHASE_G.md) · Standing bar: [`FIDELITY.md`](FIDELITY.md)  
**Method:** Code-trace + ephemeral fixture spot-checks (**A1** / **I1**; fixtures not committed)  
**Window-move lag:** Deferred (not in this report)

---

## Fidelity principle (primary bar)

**Standing definition for all future fidelity passes:** [`FIDELITY.md`](FIDELITY.md) (do not fork the rule here — update that file if the bar changes).

**Summary:** Interchange of RequestExplorer features in either direction with Postman — anything RE can cope with should export to and import from Postman; as RE gains features, re-check those paths and refresh this matrix.

---

## Must-fix under that principle

| # | Gap | Status |
|---|-----|--------|
| 1 | **Disabled query params omitted on export** | Fixed — `url.query` includes disabled rows with `"disabled": true` |
| 2 | **Import url as string throws** | Fixed — accepts url string or object |
| 3 | **Body ContentType / language lossy** | Fixed — language-ContentType mapping; import prefers Content-Type header when present |
| 4 | **Interleaved folder/request order** | Fixed — `ChildOrder` on models; tree + Postman `item[]` preserve interleave |
| 5 | **Folder/request ids on Postman items** | Fixed — export/import `item.id` (GUID) |

**Not must-fix under this principle (still open as product expansion):** form-data / oauth / scripts / descriptions; Quick Export snippet quirks; backup dirty/prefs behaviour; unknown HTTP methods outside RE verb enum; window-move lag.

---

## Scope locked for this report

| Decision | Choice |
|----------|--------|
| Surfaces | Request + collection/folder variables + names / ids / order (**B1**) |
| Paths | Local save/load, Postman export/import, Edit-as-JSON, Quick Export, backup restore (**C4**) |
| Postman-only / no UI | Listed (**D1**) |
| Fixes in Phase G | Analysis was **L1**; must-fix list later applied (2026-10-05) |

### Status codes

| Code | Meaning |
|------|---------|
| **Full** | Survives with equivalent semantics |
| **Partial** | Survives with lossy mapping |
| **Dropped** | Present in source but discarded |
| **N/A — no UI** | External/Postman feature; RequestExplorer has no editor |
| **UI-only** | Session/editor state; not part of persisted model |
| **Unsupported export** | RE has data but export path cannot represent it |
| **Fail** | Path throws / aborts (worse than silent drop) |

### Severity

| Level | Meaning |
|-------|---------|
| **S0** | Silent loss (or structural reshape) of RE user data on **local** save/reload |
| **S1** | Loss or failure on **Postman** export/import for data RE can edit, or import of common Postman shapes |
| **S2** | Lossy but uncommon / edge mapping |
| **S3** | Postman-only feature, no RE UI (expected drop) |
| **Info** | By design / session-only |

---

## Executive findings

1. **S0 — Sibling interleave:** The tree allows mixed folder/request order (e.g. `F1, R1, F2`). Persistence stores `folders[]` then `requests[]` separately. Reload always rebuilds as **all folders, then all requests** (`F1, F2, R1`). Confirmed by spot-check.
2. **S1 — Disabled query params on Postman export:** Disabled headers export with `"disabled": true`. Disabled **params** are omitted entirely from `url.query` / `raw` (`BuildUrl` filters enabled-only). Confirmed: `drop` param absent from export JSON; `X-Drop` header present.
3. **S1 — Postman `url` as string inside request object:** Import deserializes `url` as `PostmanUrl` object only. Common Postman shape `"url": "https://…"` throws `JsonException` and fails the import. (Top-level `request` as a bare URL string is handled.)
4. **S1/S2 — Body ContentType ↔ Postman `language`:** Export maps any ContentType containing `json` → `language: json`, else `text` (loses `text/plain`, `application/xml`, etc.). Import sets `application/json` only when `language == json`; otherwise leaves default **`application/json`**, so `language: text` imports incorrectly as JSON ContentType. Spot-check: `IMPORT_text_ContentType=application/json`.
5. **S3 — Large Postman surface unused:** form-data / urlencoded / file / graphql bodies, oauth*/digest/aws/… auth, scripts/events, descriptions, collection/folder auth, examples — no RE UI; import drops or maps to empty/None.
6. **Local field coverage is otherwise strong** for the current model (ids, names, verbs in enum, params/headers enabled flags, auth None/Bearer/Basic/ApiKey, raw body + ContentType string, variables).
7. **Backup restore** replaces the collections directory from the zip (including `collection-order.json`) — equivalent to on-disk state at backup time. Unsaved dirty edits and preferences are out of band (by Phase E design).
8. **Edit-as-JSON** uses the same internal model as local persistence — same strengths and the same sibling-order structural limit.

---

## 1. RequestExplorer-editable surfaces

| Field / surface | In RE UI? | Local save/load | Postman export | Postman import | Severity | Notes / code locus |
|-----------------|-----------|-----------------|----------------|----------------|----------|--------------------|
| Collection `Id` | Implicit | **Full** | **Partial** (`info._postman_id`) | **Full** if GUID parseable | S2 | `PostmanCollectionMapper.ToCollectionDocument` / `FromCollectionDocument` |
| Collection `Name` | Yes | **Full** | **Full** (`info.name`) | **Full** | — | |
| Collection variables (key/value/enabled) | Yes | **Full** | **Full** (`variable[]`, `disabled`) | **Full** | — | Blank-key rows not persisted (spare UX) — **Info** |
| Folder `Id` | Implicit | **Full** | **Dropped** (no item id) | New GUID | S2 | Nested folders never emit Postman item ids |
| Folder `Name` | Yes | **Full** | **Full** (`item.name`) | **Full** | — | |
| Folder variables | Yes | **Full** | **Full** | **Full** | — | |
| Request `Id` | Implicit | **Full** | **Dropped** (not on item; only used if exporting a lone request as collection `info._postman_id`) | New GUID | S2 | Spot-check: collection id kept, request id new |
| Request `Name` | Yes | **Full** | **Full** | **Full** | — | |
| Verb (`GET`…`OPTIONS`) | Yes | **Full** | **Full** (`method`) | **Partial** — unknown → **GET** | S2 | Spot-check: `PROPFIND` → `GET` |
| Address (URL base) | Yes | **Full** | **Full** (`url.raw` + structured parts when absolute) | **Full** when `url` object / raw; **Fail** when `url` is a JSON string | **S1** | `FromRequestItem` → `Deserialize<PostmanRequestObject>`; spot-check `URL_STRING_FAIL` |
| Params key/value | Yes | **Full** | **Partial** — enabled only in `url.query`/`raw` | **Full** from `url.query` | **S1** | Export: `BuildUrl` enabled filter; import strips `?` from Address when query array present |
| Params `IsEnabled` | Yes | **Full** | **Dropped** for disabled rows | **Full** (`disabled`) | **S1** | Spot-check: disabled import OK; disabled export missing |
| Headers key/value/`IsEnabled` | Yes | **Full** | **Full** (`disabled`) | **Full** | — | Spot-check: `X-Drop` present on export |
| Auth type None | Yes | **Full** | Omits `auth` | Maps `null`/`noauth` → None | — | |
| Auth Bearer token | Yes | **Full** | **Full** | **Full** | — | |
| Auth Basic user/pass | Yes | **Full** | **Full** | **Full** | — | |
| Auth ApiKey name/value/`in` | Yes | **Full** | **Full** (`in`: header/query) | **Full** (unknown `in` → header) | S2 | |
| Unused auth fields when type changes | Yes (stale in model) | **Full** (still on disk) | Only active type exported | Only active type imported | Info | Not cleared on type switch |
| Body raw `Content` | Yes | **Full** | **Full** when non-empty (`mode: raw`) | **Full** for `mode: raw` only | — | Empty body omitted on export for GET/HEAD/OPTIONS/DELETE |
| Body `ContentType` | Yes | **Full** | **Partial** → `options.raw.language` json\|text only | **Partial** — json→`application/json`; text leaves default `application/json` | **S1** | `BuildBody` / `FromRequestItem`; spot-check text→`application/json` |
| Sibling order among folders | Yes (tree) | **Partial** — order within `folders[]` kept | **Partial** — folders block first | **Partial** — folders then requests | S2 | |
| Sibling order among requests | Yes (tree) | **Partial** — order within `requests[]` kept | **Partial** — after folders | **Partial** | S2 | |
| **Interleaved folder/request sibling order** | Yes (tree DnD/move) | **Dropped** — reload = folders then requests | **Dropped** — same split in `ToItems` | **Dropped** — `ApplyItems` appends by kind | **S0** | `CollectionTreeMapper.ToViewModel` / `SyncModelFromView`; spot-check `TREE=F1,R1,F2` → `RELOAD=F1,F2,R1` |
| Collection order among roots | Yes | **Full** (`collection-order.json`) | N/A (one document) | N/A | — | |
| Blank spare KV rows | Yes | Not persisted (normalized out) | Not exported | N/A | Info | `NormalizeKeyValueSpares` on save |

---

## 2. Postman-only / no RE UI (**D1**)

| Field / surface | In RE UI? | Local | Postman export | Postman import | Severity | Notes |
|-----------------|-----------|-------|----------------|----------------|----------|-------|
| Body `urlencoded` | No | N/A | Never emitted | **Dropped** (body empty) | S3 | Spot-check formdata empty; urlencoded same path |
| Body `formdata` | No | N/A | Never | **Dropped** | S3 | |
| Body `file` / `graphql` | No | N/A | Never | **Dropped** | S3 | |
| Auth oauth1 / oauth2 / digest / awsv4 / ntlm / hawk / edgegrid | No | N/A | Never | → **None** | S3 | Spot-check oauth2 → `None` |
| Collection / folder level `auth` | No | N/A | Never | **Dropped** (not on models) | S3 | |
| `event` / scripts (prerequest, test) | No | N/A | Never | **Dropped** | S3 | Not on `PostmanItem` / document models |
| Descriptions (info/item/request) | No | N/A | Never | **Dropped** | S3 | |
| KV `description` / non-string variable types | No | N/A | Variables forced `type: string` | Type ignored | S3 | |
| `protocolProfileBehavior`, certificates, examples/saved responses | No | N/A | Never | **Dropped** | S3 | |
| Request-level `item.variable` | No | N/A | Never | **Dropped** | S3 | `ApplyItems` ignores variables on request items |
| Methods outside RE enum | No | N/A | N/A | → **GET** | S2 | |
| Postman `url` JSON string (in request object) | N/A | N/A | RE emits object/`raw` | **Fail** (`JsonException`) | **S1** | Common Postman export shape |

---

## 3. Edit-as-JSON (Phase F)

Internal camelCase model via `SubtreeJsonService` — same shape as disk JSON.

| Concern | Status | Notes |
|---------|--------|-------|
| Round-trip of RE model fields | **Full** (same as local) | Serialize/deserialize uses same options family as persistence |
| Root id preserve on Apply | **Full** (D1 Phase F) | Forced to original root id |
| Child array order in JSON | **Full** within `folders[]` / `requests[]` | Cannot express interleaved tree order — model limit (**S0** structural) |
| Unknown JSON properties | Ignored | STJ default |
| Session LastResponse after Apply | Dropped (new VMs) | By Phase F **J1** design — **UI-only** |

---

## 4. Quick Export (`RequestExportService`)

Runnable snippets (curl, HTTP, Fetch, PowerShell, Python, HTTPie) + optional single-request Postman Collection format.

| Concern | Status | Severity | Notes |
|---------|--------|----------|-------|
| Enabled params in URL | **Full** (for snippet) | Info | Disabled params intentionally omitted |
| Disabled headers | Omitted | Info | Snippets emit effective wire view |
| Auth Bearer/Basic/ApiKey | Applied into headers/query | — | `GetEffectiveHeaders` / `BuildUrl` |
| Body + Content-Type | Included when body non-empty | — | Defaults Content-Type to `application/json` if blank |
| Postman Collection format in Quick Export | Same mapper as file export | S1/S2 | Inherits disabled-param + ContentType language limits |
| HTTP format | Requires absolute URL (`new Uri`) | S2 | Relative/token URLs can throw at export time |
| Variable tokens `{{x}}` | Passed through (escaped carefully) | Info | Not expanded — diagnosis is separate UI |

Quick Export is **not** a persistence format; treat gaps as documentation for “copy as curl” accuracy, not data loss on disk.

---

## 5. Backup restore (`WorkspaceBackupService`)

| Concern | Status | Severity | Notes |
|---------|--------|----------|-------|
| Collections directory bytes | **Full** vs on-disk at backup time | — | Zip `collections/` swapped in place |
| `collection-order.json` | **Full** if present in backup | — | Lives under collections dir |
| Unsaved dirty editor state | **Dropped** (not in backup) | Info | Phase E **D1** — warn on Backup now |
| `preferences.json` in zip | Archived but **not** applied by in-app Restore | Info | Phase E **G2** |
| After restore | Reload workspace from disk | — | Same loader as normal start/reload |

**Equivalence note:** Restore ≡ replacing collections files with a prior on-disk snapshot. Fidelity of individual fields equals **local save/load** for whatever was last persisted into that snapshot. It does not invent a second mapping layer.

---

## 6. Spot-checks performed (ephemeral)

| Check | Result |
|-------|--------|
| Disabled param `drop` on Postman export | Absent (`EXPORT_HAS_drop_param=False`) |
| Disabled header `X-Drop` on export | Present |
| `ContentType=text/plain` export language | `"language": "text"` |
| Import `language: text` → ContentType | `application/json` (wrong default) |
| Interleaved tree `F1,R1,F2` after sync/reload | `F1,F2,R1` |
| Import `formdata` body | Empty content |
| Import `oauth2` | `AuthType.None` |
| Import disabled query param | `IsEnabled=false`, kept |
| Import method `PROPFIND` | `GET` |
| Collection id export→`FromCollectionDocument` | Preserved; request id new |
| `"url": "https://…"` string in request object | `JsonException` |

---

## 7. Suggested fix priority (**K1**)

P0/P1 must-fix items from this pass are **implemented** (2026-10-05). Remaining rows are future product work.

| Priority | Item | Status |
|---------:|------|--------|
| **P0** | Postman import: url string or object | Done |
| **P0** | Postman export: disabled query params | Done |
| **P0** | Body ContentType / language round-trip | Done |
| **P0** | Interleaved folder/request order (local + Postman) | Done |
| **P1** | Postman item ids for folders/requests | Done |
| **P2** | Unknown HTTP methods / extend verb set | Open — only if RE gains the capability first |
| **P3** | form-data, oauth, scripts, descriptions, … | Open — product expansion |
| **Later** | Window-move lag investigation | Deferred (**F3**/**M2**) |

---

## 8. Code map (quick reference)

| Path | Entry points |
|------|----------------|
| Local save | `MainViewModel.SaveCollection` → `NormalizeKeyValueSpares` → `CollectionTreeMapper.SyncModelFromView` → `CollectionPersistenceService.Save` |
| Local load | `CollectionPersistenceService.LoadAll` → `CollectionTreeMapper.ToViewModel` |
| Postman export (file) | `MainViewModel.ExportNode` → `PostmanCollectionMapper.ToCollectionDocument*` |
| Postman import (UI) | `FromCollectionDocumentAsFolder` into selected collection/folder (not top-level `FromCollectionDocument` in UI) |
| Edit-as-JSON | `SubtreeJsonService` + `MainViewModel.TryApplySubtreeJson` |
| Quick Export | `RequestExportService.Export` |
| Backup restore | `WorkspaceBackupService.RestoreCollections` → `ReloadWorkspaceFromDisk` |

---

## 9. Out of scope / explicitly not gaps

- Session-only: LastResponse, undo, dirty asterisk, expand/collapse, filter text, window bounds  
- Replay mode (Phase Z)  
- P0/P1 must-fix items from this pass (implemented 2026-10-05)
