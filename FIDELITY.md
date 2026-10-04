# RequestExplorer — Postman fidelity (standing definition)

**Status:** Standing policy (recorded 2026-10-05)  
**Use for:** Every future fidelity pass, and whenever a new RE feature touches persisted request/collection data.  
**Latest gap snapshot:** [`FIDELITY_GAPS.md`](FIDELITY_GAPS.md) (Phase G; must-fix items applied 2026-10-05 — update or supersede on the next pass)

---

## Primary bar

**Interchange of RequestExplorer features in either direction with Postman** (Collection format v2.1).

| Direction | Rule |
|-----------|------|
| **RE → Postman** | Anything RE can store/edit should be **exportable** into Postman Collection JSON without dropping that data. |
| **Postman → RE** | Anything in a Postman collection that maps onto an RE-capable feature should be **importable** into that feature (no throw, no silent wipe of that field). |

**In scope:** Features RE already has (or newly adds) — e.g. URL/address, params/headers including disabled flags, verbs RE supports, auth types RE supports, body modes RE supports, ContentType, variables, names, ids where Postman can carry them, tree/item order.

**Out of scope for “interchange bug”:** Postman-only surfaces RE cannot cope with yet. Those are **product expansion** candidates, not failures of this bar, until RE gains the matching feature — at which point they fall under the bar and must export/import.

**Local save/load** remains a correctness baseline for the same RE model. It does not replace the Postman interchange check.

---

## When RE gains a feature

For each new persisted editor/model capability, before calling the feature done:

1. **Local:** Survives save → reload.  
2. **Export:** Appears in Postman Collection JSON with equivalent semantics (or an explicit, documented lossless mapping).  
3. **Import:** Postman JSON that uses the corresponding construct loads back into the RE feature (no exception, no silent drop).  
4. **Update the gap notes** ([`FIDELITY_GAPS.md`](FIDELITY_GAPS.md) or a successor pass report) so the matrix stays current.

Session-only UI (e.g. last response, undo, filter text) is outside this bar unless you deliberately decide otherwise.

---

## Related

- Phase G plan: [`PLAN_PHASE_G.md`](PLAN_PHASE_G.md)  
- Backlog Decision #9 / cross-cutting principle: [`PLAN_IMPROVEMENTS_BACKLOG.md`](PLAN_IMPROVEMENTS_BACKLOG.md)  
- Mapper: `RequestExplorer/Services/Postman/PostmanCollectionMapper.cs`
