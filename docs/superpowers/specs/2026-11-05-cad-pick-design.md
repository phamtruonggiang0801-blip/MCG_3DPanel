# Pick in drawing → grid row, Esc clears — design (2026-11-05)

## Why
The engineer wants to click an object in the drawing and find its row on the grid (Result and Naming tabs), and Esc
to drop the highlight everywhere.

## Decisions (engineer, 2026-11-05)
1. **Pick button** on the Result and Naming toolbars (and in the right-click menu). Click objects in the drawing; each
   pick selects its row on the **active** tab, scrolls to it and highlights. Further picks **add** to the selection.
   *My call:* Enter / right-click ends picking and keeps the selection; Esc ends picking and clears everything.
2. "Find on click" without a button: **deferred**.
3. The tab that reacts is the one in front.
4. A picked object with no row: **no message**. *My call:* on Naming a flange picks its web line.
5. **Esc clears the highlight on both sides:** Esc in the drawing (no command running) and Esc on the grid (not while
   typing a name) clear the CAD highlight and the grid selection of both tabs.

## Approaches
- Nested pick (`Editor.GetNestedEntity`, like NENTSEL) — **chosen**: exact nested solid / polyline.
- Pickfirst selection — rejected: a plain click selects the whole block, not the nested part.

## Design
- `Commands/CadPick.cs`: `PickLoop(Func<ObjectId, bool> onPick, Action onClear)` — loop of `GetNestedEntity`
  (`AllowNone`), OK ⇒ `onPick(entity id)`, None ⇒ stop, Cancel ⇒ `onClear()`.
- Esc in the drawing: `Application.PreTranslateMessage` (WM_KEYDOWN, VK_ESCAPE) while the active document has no
  command in progress ⇒ `PaletteManager.ClearAllSelections()` (posted to the dispatcher).
- Tabs: `bool SelectFromCad(ObjectId picked)` (adds the row, scrolls, returns false when there is no row) and
  `void ClearSelection()`. Result matches `row.Solid.SolidId`; Naming matches the last id of a piece path (pieces
  and flanges) in a panel of the active drawing.
- Grid `PreviewKeyDown` Esc (not inside a TextBox) ⇒ clear both tabs.
- Tests: the row-matching helper is pure only partially; the rest is AutoCAD UI — verified with checklist rows H1–H6.
