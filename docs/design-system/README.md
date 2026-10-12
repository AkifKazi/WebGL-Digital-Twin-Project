# Design system site

An interactive reference for the UI design system: tokens, text styles, and every component in every
state, drawn by a WebGL 2 port of `Assets/Materials/UI/UIShape.shader`. Hover, press, Tab through
the live components; click a specimen to open its style in the inspector and move its sliders; click
a colour swatch to re-theme the whole page; switch to the *Bright scene* backdrop to see why shapes
carry a sharp shadow as well as a glow.

## Open it

- **From disk (Drive, a zip, an email):** double-click `index.html`. Nothing is fetched: data, fonts
  and icons are embedded, so it works offline and over `file://`.
- **Hosted:** it is a static folder. On GitHub Pages choose *Deploy from a branch*, `main`, folder
  `/docs`, and the site is at `/design-system/`.

Needs a browser with WebGL 2 (any current Chrome, Edge, Firefox or Safari). Without it the page shows
the static catalog image instead.

## Keep it in step with Unity

`data.js` and `fonts.css` are generated from the Unity assets; do not edit them by hand.

```bash
python3 Tools/DesignSystemSite/export_design_system.py
```

Run it after any change to `Assets/UI/Design System` (tokens, components, styles), then commit the
two generated files. It reads the YAML directly, so Unity does not need to be open. `catalog.png` is
the fallback image; refresh it with `UIDesignSystemCatalog.BuildAndCapture` (see
`Assets/Documentation/UI_DESIGN_SYSTEM.md`).

## Files

| File | What it is |
| --- | --- |
| `index.html`, `style.css` | The page |
| `app.js` | Sections, live components, state logic (mirrors `UIComponentStyle.Resolve`), inspector |
| `renderer.js` | The shape shader in GLSL, drawn in linear light like the Unity project |
| `data.js`, `fonts.css` | Generated: tokens, components, styles, icon sprites; Rajdhani (SIL OFL) |
| `catalog.png` | Fallback for browsers without WebGL 2 |

Edits made in the inspector or the colour swatches exist only in the open page. To change the product,
edit the Unity assets and re-export.
