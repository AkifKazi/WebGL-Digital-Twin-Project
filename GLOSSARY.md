# Glossary

Domain terms for the digital twin, as the code and the docs use them.

**Layout** — one arrangement of the whole interface. The *wide layout* serves a desktop browser and a phone held sideways. The *portrait layout* serves a phone held upright. The responsive shell shows exactly one at a time.

**Card** — one sensor's telemetry tile: its name, value, unit and alarm state.

**Card rail** — a strip beside the model that holds cards: a *column* left or right of it, or a *row* above or below it. Each side has an outer rail and, when needed, inner rails. Each layout has one `StatRailManager` that owns all of its card rails. In the portrait layout the rails always run as rows. In the wide layout they run as columns, or as rows in a window taller than it is wide.

**First / second edge** — the two sides that rails stack in from: left and right for columns, top and bottom for rows.

**Detail rail** — a rail of its own between the card rails and the model, where a card's history panel opens. It never holds cards. The portrait layout has no detail rail; there the history opens as a sheet.

**Layout profile** — what one screen shape asks of the rails: rows or columns, how many rails, whether the history panel opens, and the room kept for the model and the panel. The responsive shell resolves it from the Telemetry Layout Settings and hands it to each layout's `StatRailManager`.

**Leader line** — the line from a card to its sensor on the model. Each layout has one `StatLeaderLineManager` that draws them for its card rails.

**View** — one of the two top-level views: *Telemetry* (cards, leader lines, history) or *Service* (the service panel and the pins on the model). The chosen view is shared by both layouts, so turning a phone keeps it.

**Inspection view** — how the model itself is shown: *Full Model*, *Cross Section* or *X-Ray*. `MachineViewModeController` owns it. Full Model hides telemetry; Cross Section and X-Ray show it.

**Telemetry visibility** — how far a layout's telemetry shows, from three inputs: the view (and the blend while switching), the inspection view, and, in the portrait layout, the sheet's cover. Each layout has one `TelemetryVisibility` that owns its view and combines the three. Everything that hides or reveals telemetry asks it, never the inputs directly.

**Telemetry region** — a part of a layout's interface that holds only telemetry and fades as one: in the wide layout the content area, the leader line layer and the top controls; in the portrait layout the content area and the bottom controls.
