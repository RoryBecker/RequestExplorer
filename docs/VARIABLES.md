# Variables

RequestExplorer resolves `{{name}}` tokens from **enabled** variables on the request’s ancestor collections and folders (nearest definition wins, case-insensitive).

## Using variables

1. Add a row on a collection or folder **Variables** grid (key + value).
2. Reference it in the URL, params, headers, body, or auth fields as `{{key}}`.
3. On **Send** (and in Quick Export / tooltips), tokens are substituted from the parent scope.

Tick **Secret** to keep the value out of **To Postman** / **To Postman (Flatten)** (the key is still exported, empty). Local save, send, copy/paste, and duplicate inside RequestExplorer keep the value. Default is off. The collections folder on disk is still the working copy, so it still contains secret values.

**Diagnose Variables** (tree context menu) reports missing or disabled names under the selection.

### Hover tooltips

| Where you hover | What you see |
|-----------------|--------------|
| Variable **name** (key) in Local or **Inherited** Variables grids | `name =` current calculated value (recipes such as `@date(today-7d)` are evaluated for the tooltip) |
| Params / Headers **key** | That row’s value (recipes evaluated if present) |
| A `{{name}}` token in URL, body, auth, etc. | That variable’s resolved value from scope |

## More

- [Variables - Dynamic](VARIABLES-DYNAMIC.md) — `@date(…)` recipes; the stored value stays the recipe
- [Variables - Refactoring](VARIABLES-REFACTORING.md) — extract, replace, and apply tokens into URLs
