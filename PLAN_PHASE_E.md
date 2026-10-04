# Phase E — Data safety (plan)

**Status:** Decisions recorded + Phase E approved (2026-10-05) — awaiting **implement** instruction  
**Created:** 2026-10-05  
**Updated:** 2026-10-05 (answers + approval)  
**Parent backlog:** [`PLAN_IMPROVEMENTS_BACKLOG.md`](PLAN_IMPROVEMENTS_BACKLOG.md)  
**Repo policy:** Human owns all commits. Agent does not commit.

---

## Scope (from backlog Phase E)

| # | Item | Backlog ref |
|---|------|-------------|
| E1 | Rolling **backup** of app data (startup + on demand); prune to last **N** | Item 11 + Decision #6 |
| E2 | **Restore** UI from a chosen backup (destructive; confirm) | Item 11 |
| E3 | Extend **Settings** with backup prefs; keep existing timeout / dev mode | Item 11 (+ Item 12 already shipped) |

**Out of scope for Phase E:** per-collection restore, cloud sync, encrypted backups, Replay (Z), modify-as-JSON (F), fidelity report (G).

---

## Decisions (2026-10-05)

| Q | Code | Decision |
|---|------|----------|
| Q1 | **A1** | Retention N: min 1, max 100, default **15**. |
| Q2 | **B1** | Backup on startup: **on** by default. |
| Q3 | **C2** | Startup backup **after UI shown**, in background (status “Backing up…”). |
| Q4 | **D1** | Backup now uses **on-disk only**; warn if unsaved changes are not included. |
| Q5 | **E2** | Each backup is a **single zip archive** (not a loose folder tree). |
| Q6 | **F1** | Dirty before Restore: Save / Discard / Cancel (same as Reload). |
| Q7 | **G2** | In-app restore applies **collections only**; leave current preferences untouched. |
| Q8 | **H1** | Always create a **pre-restore** safety backup, then prune. |
| Q9 | **I1** | Menu items under **File** (near Open collections folder / Reload). |
| Q10 | **J2** | Restore picker: list with timestamp + reason **and** approximate size / collection count when cheap. |
| Q11 | **K2** | After restore: reload collections only for workspace data; **keep current window placement** (do not re-apply layout from backup prefs — consistent with G2). |
| Q12 | **L1** | Implement full E1–E3 scope; nothing dropped. |
| Q13 | **M1** | `backup-meta` (inside zip): **app version** only (+ timestamp/reason as needed for the list). |

Also locked from backlog:

| Topic | Decision |
|-------|----------|
| Scope | Both rolling backup and restore UI |
| Settings | Retention N + startup toggle live with existing timeout / dev mode |
| Destructive ops | Restore needs clear confirmation |
| Commits | Human only |

---

## Current state (code snapshot)

### On-disk layout today

| Path | Contents |
|------|----------|
| `%LocalAppData%\RequestExplorer\collections\` | One `{guid}.json` per collection + `collection-order.json` |
| `%LocalAppData%\RequestExplorer\preferences.json` | UI prefs (timeout, dev mode, layout, filters, …) |

- `CollectionPersistenceService` / `UiPreferencesService` as today.
- **Tools → Settings…** already edits timeout + dev mode.
- **File → Open collections folder**, **Reload from Disk** (dirty confirm) — restore mirrors reload after swapping collection files.
- No backup folder or backup/restore commands yet.

### What is backed up

Zip contents always include **collections/** + **preferences.json** + **backup-meta.json** (app version, created time, reason).  
**In-app Restore (G2)** writes back **collections only**; preferences in the zip remain for manual recovery / future use but are not applied by the Restore command.

Session-only state (last responses, undo, search) is not backed up.

---

## E1 — Rolling backup

### Intent

Automatically and manually snapshot recoverable copies; keep only the newest **N** zips.

### Proposed layout (**E2** = zip)

```text
%LocalAppData%\RequestExplorer\
  collections\
  preferences.json
  backups\
    20261005-194530.zip       # local timestamp yyyyMMdd-HHmmss
    20261005-194530-2.zip     # suffix if same-second collision
```

Zip entry layout (internal):

```text
collections/…                # full copy of collections dir
preferences.json
backup-meta.json             # createdUtc, reason (startup|manual|pre-restore), appVersion (M1)
```

### When backups run

| Trigger | Behaviour |
|---------|-----------|
| **Startup** (**C2**) | After main window is shown: if `BackupOnStartup`, run backup on background thread; status “Backing up…” → done/error; then prune. |
| **Backup now** (**D1**) | Snapshot **current on-disk** files; if any collection dirty, warn that unsaved edits are not included; then prune. |
| **Before restore** (**H1**) | Always create a `pre-restore` zip of current disk collections (+ prefs for archive completeness), then prune. |

### Prune (**A1**)

- After each successful backup: list `backups\*.zip`, newest first, delete beyond **N** (`BackupRetentionCount`, default 15, clamp 1–100).
- Failed backup must not delete older good backups.

### Implementation sketch

- `WorkspaceBackupService`:
  - `CreateBackupAsync(reason)` → zip path (stage temp zip, then move into `backups\`)
  - `Prune(retention)`
  - `ListBackups()` → timestamp, reason, appVersion, zip size, collection file count (from zip central directory or meta)
  - `RestoreCollections(backupZip)` → extract collections to staging, replace live `collections\` (prefs file untouched)
- Startup: queue after Loaded / first idle; do not block first paint (**C2**).
- Use `System.IO.Compression.ZipFile` (or equivalent).

### Risks

- Background startup backup overlapping first Save — coordinate with a simple lock/mutex on backup+restore operations.
- Zip of large workspaces still costs CPU/disk; status message sets expectation.
- Dirty warn (**D1**) must be visible but not block backup if user confirms / proceeds (warn-only vs confirm — v1: warn in status or non-blocking dialog, still create backup).

---

## E2 — Restore

### Intent

Pick a backup zip and replace **live collections** with its contents; confirm clearly; do not touch preferences (**G2** / **K2**).

### Proposed UX (**I1**, **J2**, **F1**, **H1**)

**File** menu:

- **Backup now**
- **Restore…**
- **Open backups folder**

**Restore…** flow:

1. If dirty: Save / Discard / Cancel (**F1**).
2. Dialog listing zips (newest first): timestamp, reason, app version, **size**, **collection count** (**J2**).
3. Confirm: replace collections on disk from backup `{name}`? Preferences will not change. Cannot undo except by restoring another backup.
4. Create **pre-restore** safety zip (**H1**), prune.
5. Extract collections from chosen zip → stage → replace live `collections\`; **reload** tree; **clear undo**; leave window placement and in-memory/current prefs as-is (**K2** / **G2**).
6. Status on success; error dialog on failure (prefer failed restore leaves previous collections intact via staging).

### Implementation sketch

- `RestoreBackupDialog` with list columns for meta + size + count.
- Shared reload path with Reload from Disk (`clearUndo: true`).
- Do **not** reload `preferences.json` into `UiPreferences` / window bounds on restore.

### Risks

- Partial extract — stage directory then swap.
- User confusion that prefs in the zip are “ignored” by Restore — confirm copy should say so.

---

## E3 — Settings extensions

### Intent

Configure backup behaviour beside existing timeout / dev mode.

### Proposed Settings additions

| Control | Pref key | Default |
|---------|----------|---------|
| Backup on startup | `BackupOnStartup` | **true** (**B1**) |
| Retention count (N) | `BackupRetentionCount` | **15** (clamp 1–100) (**A1**) |
| Hint | Backups folder path (read-only) | `%LocalAppData%\…\backups` |

Widen dialog for the Backups section. OK persists via existing prefs path.

---

## Delivery order within Phase E

1. **E1** zip service + prefs + prune + Backup now + Open backups folder + background startup  
2. **E3** Settings UI fields  
3. **E2** Restore dialog + confirm + collections-only reload  

---

## Acceptance criteria

- [ ] **Backup now** creates `backups\yyyyMMdd-HHmmss.zip` containing collections + preferences + meta (app version).
- [ ] Unsaved dirty → warn that backup is on-disk only (**D1**).
- [ ] Startup backup runs after UI show when enabled; status updates; prune ≤ **N** zips (**C2**, **A1**, **B1**).
- [ ] **Restore…** lists size + collection count; confirms; pre-restore zip; replaces collections only; prefs/window placement unchanged; undo cleared (**J2**, **H1**, **G2**, **K2**, **F1**).
- [ ] Settings: startup toggle + retention; timeout/dev mode unchanged.
- [ ] **Open backups folder** works; failures surface clearly.
- [ ] Menu under **File** (**I1**).
- [ ] No agent commits.

---

## Answer key (recorded)

| Q | Choice |
|---|--------|
| Q1 | A1 |
| Q2 | B1 |
| Q3 | C2 |
| Q4 | D1 |
| Q5 | E2 |
| Q6 | F1 |
| Q7 | G2 |
| Q8 | H1 |
| Q9 | I1 |
| Q10 | J2 |
| Q11 | K2 |
| Q12 | L1 |
| Q13 | M1 |

---

## Next step

Phase E is **approved** with the decisions above. Reply **implement Phase E** (or similar) when you want coding to start.
