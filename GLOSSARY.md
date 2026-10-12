# Glossary

Domain terms for the digital twin, as the code and the docs use them.

**Layout** — one arrangement of the whole interface. The *wide layout* serves a desktop browser and a phone held sideways. The *portrait layout* serves a phone held upright. The responsive shell shows exactly one at a time.

**Card** — one sensor's telemetry tile: its name, value, unit and alarm state.

**Card rail** — a strip beside the model that holds cards: a *column* left or right of it, or a *row* above or below it. Each side has an outer rail and, when needed, inner rails. Each layout has one `StatRailManager` that owns all of its card rails. While the layout has a detail rail, every inner rail holds one card fewer than the outermost one, so its cards have room to step aside for a *connector*. In the portrait layout the rails always run as rows. In the wide layout they run as columns, or as rows in a window taller than it is wide. Warning and critical cards take the innermost rail shown on their side, nearest the model, so their always-drawn lines are the shortest.

**First / second edge** — the two sides that rails stack in from: left and right for columns, top and bottom for rows.

**Detail rail** — a rail of its own between the card rails and the model, where a card's history panel opens. It never holds cards. The portrait layout has no detail rail; there the history opens as a sheet.

**Layout profile** — what one screen shape asks of the rails: rows or columns, how many rails, whether the history panel opens, and the room kept for the model and the panel. The responsive shell resolves it from the Telemetry Layout Settings and hands it to each layout's `StatRailManager`.

**Leader line** — the line from a card to its sensor on the model. Each layout has one `StatLeaderLineManager` that draws them for its card rails. Warning and critical lines are always drawn; others only while their card is focused. While a card's detail panel is open, its line leaves from the panel, and a *connector* runs straight from the card to the panel, so it is plain which card the panel belongs to. Cards on the rails the connector crosses step aside for it; where one cannot, the connector fades across it. A line fades wherever it shows through translucent UI: another card, or a control marked as a *leader line obstacle* (view mode, quality, zoom, pagination, the Telemetry / Service switch). It never fades for the opaque detail panel, which it simply runs behind.

**View** — one of the two top-level views: *Telemetry* (cards, leader lines, history) or *Service* (the service panel and the pins on the model). The chosen view is shared by both layouts, so turning a phone keeps it.

**Inspection view** — how the model itself is shown: *Full Model*, *Cross Section* or *X-Ray*. `MachineViewModeController` owns it. Full Model hides telemetry; Cross Section and X-Ray show it.

**Telemetry visibility** — how far a layout's telemetry shows, from three inputs: the view (and the blend while switching), the inspection view, and, in the portrait layout, the sheet's cover. Each layout has one `TelemetryVisibility` that owns its view and combines the three. Everything that hides or reveals telemetry asks it, never the inputs directly.

**Telemetry region** — a part of a layout's interface that holds only telemetry and fades as one: in the wide layout the content area, the leader line layer and the top controls; in the portrait layout the content area and the bottom controls.

**Card focus** — which card is focused, and which cards have their detail open. Only one card is ever focused. The first press on a card focuses it: the part is marked on the model and the other cards and their lines dim (alarm cards never do). The next press opens its detail. In the wide layout a click opens the detail at once, while a tap only focuses, unless a panel is already open, which then moves to the tapped card; the portrait sheet always waits for the second press. A click on the scene, Esc, or closing the panel releases the focus. `CardFocus` holds the one card focus for the whole twin, and its rules live in the plain `CardFocusState`. The detail panels and the sheet are its *detail presenters*.

**Detail presenter** — what opens a card's detail in one layout: the wide layout's panels, or the portrait layout's sheet. Card focus asks the one that owns a pressed card what the press does.
