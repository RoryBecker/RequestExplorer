# Variables

RequestExplorer resolves `{{name}}` tokens from **enabled** variables on the request’s ancestor collections and folders (nearest definition wins).

## Using variables

1. Add a row on a collection or folder **Variables** grid (key + value).
2. Reference it in the URL, params, headers, body, or auth fields as `{{key}}`.
3. On **Send** (and in Quick Export / tooltips), tokens are substituted from the parent scope.

**Diagnose Variables** (tree context menu) reports missing or disabled names under the selection.

### Hover tooltips

| Where you hover | What you see |
|-----------------|--------------|
| Variable **name** (key) in Local or **Inherited** Variables grids | `name =` current calculated value (recipes such as `@date(today-7d)` are evaluated for the tooltip) |
| Params / Headers **key** | That row’s value (recipes evaluated if present) |
| A `{{name}}` token in URL, body, auth, etc. | That variable’s resolved value from scope |

## Dynamic value recipes

A variable’s **stored value** can be a recipe. The grid keeps the recipe text; resolution turns it into a concrete string for scope, tooltips, and send.

Default calendar / clock is **UTC** unless `local` is specified.

### Date-only — `today`

Default format: `yyyy-MM-dd`.

| Recipe | Result |
|--------|--------|
| `@date(today)` | Today’s UTC date |
| `@date(today-7d)` | Seven UTC days before today |
| `@date(today+3d)` | Three UTC days after today |
| `@date(today-7d, yyyyMMdd)` | Offset with a custom format string |
| `@date(today, local)` | Today in the **local** time zone |
| `@date(today, yyyy-MM-dd, UTC)` | Explicit UTC (same as omitting the zone) |
| `@date(today, yyyy-MM-dd, local)` | Format + local zone |

### Date-time — `now`

Default format (UTC): `yyyy-MM-ddTHH:mm:ssZ`  
Default format (local): `yyyy-MM-ddTHH:mm:ss`

| Recipe | Result |
|--------|--------|
| `@date(now)` | Current UTC instant |
| `@date(now-7d)` | Current UTC instant minus seven days |
| `@date(now+1d)` | Current UTC instant plus one day |
| `@date(now, o)` | UTC with a custom [.NET format](https://learn.microsoft.com/dotnet/standard/base-types/custom-date-and-time-format-strings) (here: round-trip) |
| `@date(now, local)` | Current local clock time |
| `@date(now-1d, yyyy-MM-dd HH:mm:ss, local)` | Format + offset + local |

### Syntax

```text
@date( today|now [±N d] [, format] [, UTC|local] )
```

- `today` — date-only (time stripped after applying the zone).
- `now` — full date-time (keeps the clock time).
- `±Nd` — optional whole-day offset (`-7d`, `+3d`). Spaces around the number are allowed (`- 7 d`).
- `format` — optional .NET custom date/time format (invariant culture).
- `UTC` / `local` — optional; **UTC** if omitted.

### Examples

| Key | Value |
|-----|--------|
| `weekAgo` | `@date(today-7d)` |
| `sentAt` | `@date(now)` |

```text
https://api.example.com/events?since={{weekAgo}}&at={{sentAt}}
```

If “now” (UTC) is `2026-10-19T15:30:00Z`, that becomes roughly:

```text
…?since=2026-10-12&at=2026-10-19T15:30:00Z
```

### Behaviour notes

- Recipes are evaluated when the variable scope is built (send, export, name-column hover tooltips, “replace with `{{var}}`” matching).
- If a recipe does not parse or the format string is invalid, the value is left as the original recipe text.
- Plain values (anything that is not a recognised recipe) are unchanged.

## Related code

- `RequestExplorer/Services/Variables/VariableResolver.cs` — scope + `{{…}}` substitution  
- `RequestExplorer/Services/Variables/VariableValueRecipes.cs` — `@date(…)` evaluation  
