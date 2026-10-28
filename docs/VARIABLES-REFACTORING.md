# Variables - Refactoring

Commands that turn literal URL text into `{{name}}` tokens (or the reverse extract). Matching uses the variable’s **resolved** value (including `@date(…)` recipes), skips slices that are already `{{tokens}}`, and ignores empty values. Names are case-insensitive; nearest enabled ancestor wins.

## Extract as New Variable…

On the request **URL** field, select a slice and choose **Extract as New Variable…**. That creates an enabled variable on the parent collection or folder and replaces the selection with `{{name}}`.

## Insert / Replace with {{name}}

The URL context menu always offers **Insert** and **Replace with**, listing every enabled ancestor variable (nearest first, that container’s order).

- **Insert** is enabled when the caret has no selection — it puts `{{name}}` at the caret.
- **Replace with** is enabled when text is selected — it swaps the selection for `{{name}}`.

Both stay disabled when the request has no variables further up the tree.

When selected text already matches a scoped variable’s value, the menu also offers **Replace with {{name}}** for each match.

## Apply Variables to URL…

Right-click a **request** in the tree. The dialog lists ancestor variables whose values appear in that request’s URL. Tick which literals to replace with `{{name}}`. Overlapping replacements cannot be applied together; the preview bolds injected tokens the same way as the calculated URL.

## Apply Variable to URLs…

Right-click a **Local** or **Inherited** variable row. The dialog lists descendant requests whose URLs would actually change. All rows start checked; uncheck any to skip.

- **Local** — every matching descendant request under the collection or folder you are editing.
- **Inherited** — only descendants of that same editor node, not the whole tree under the definition.
- Closer definitions that **shadow** the same name are skipped, so injecting `{{name}}` would not resolve to this row.

## Related code

- `RequestExplorer/Services/Variables/UrlVariableInjector.cs` — literal matches and token rewrite
- `RequestExplorer/Services/Variables/UrlVariableTreeApplicator.cs` — descendant request scan
- `RequestExplorer/Views/MenuHelpers/UrlExtractAsVariableMenu.cs`
- `RequestExplorer/Views/MenuHelpers/ReplaceMatchingVariableMenu.cs`
