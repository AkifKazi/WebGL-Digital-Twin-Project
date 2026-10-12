# UI design system

The digital twin's UI is built from one design system: **tokens**, **components** with their
**states**, and a few runtime pieces that apply them. Nothing is drawn from sliced sprites; every
panel, control, card and line is a `UIShape` drawn per pixel by the `UI/Digital Twin/Shape`
shader. Icons keep their authored sprites.

![Every component in every state](Images/UI%20Design%20System.png)

The look follows the Halo 5 menus, kept subtle and readable over a bright scene: dark glass
panels with a lit bottom edge, thin cyan strokes, a soft glow on what is selected, a light that
turns on from a control's bottom edge when hovered, and a sharp dark shadow under lines, icons
and text.

An interactive version, with live components and a style inspector, is in `docs/design-system/index.html`. Regenerate it with `python3 Tools/DesignSystemSite/export_design_system.py` after changing a token, component or style.

## Where everything lives

| What | Where | Edit it to |
| --- | --- | --- |
| Tokens | `Assets/UI/Design System/UI Tokens.asset` | change a colour, the spacing scale, a text style or motion time everywhere at once |
| Components | `Assets/UI/Design System/Components/<Name>/<Name>.asset` | choose which style each state uses |
| State styles | `Assets/UI/Design System/Components/<Name>/<Name> <State>.asset` | change one state's fill, stroke, glow, bottom light, shadow, corners or motion |
| Theme | `Assets/Resources/UI Theme.asset` | list the components; map sprite-authored UI to components; switch the text shadow |
| Catalog | `Assets/UI/Design System/UI Design System Catalog.unity` | see every component in every state; play it to hover the live row |

Edits show immediately in the Scene and Game views.

## Tokens

Like variables in a design tool. Styles name a colour token with an alpha (the Inspector shows a
picker, a swatch and an alpha slider), so changing **Accent** changes every edge, glow and label
that uses it.

- **Colours.** Accent, Accent Bright, Accent Deep; Warning, Critical, Neutral; Surface, Surface
  Raised, Surface Hover, Surface Inset, and a surface per alarm tone; Shadow; Text, Label;
  White for marks tinted by their owner. Each token says what it is for.
- **Spacing.** XS 4, S 8, M 12, L 16, XL 24, XXL 32 canvas units at 1920 x 1080. The component
  menu pads and spaces with these.
- **Text styles.** Display, Value, Title, Label, Button, Body, Caption: font, size, letter
  spacing, case and colour token. Put a **UI Text Style** on a text to use one.
- **Motion.** Fast (hover) and normal (selection, alarms) transition times.

## Components and states

| Group | Component | States |
| --- | --- | --- |
| Containers | Panel, Sheet, Section, Detail Panel, Inset | Idle |
| Controls | Segment | Idle, Hover, Pressed, Selected |
| | Button | Idle, Hover, Pressed, Selected, Disabled |
| | Icon Button | Idle, Hover, Pressed |
| | Icon | Idle, Hover, Disabled |
| | Focus Ring | Idle |
| | Badge | Idle, Warning, Critical, Unavailable |
| Telemetry | Card | Idle, Hover, Selected, Selected Hover, Warning, Warning Hover, Critical, Critical Hover, Unavailable |
| | List Row | Idle, Hover, Warning, Warning Hover, Critical, Critical Hover |
| Marks | Leader Line, Leader Anchor, Accent Bar, Underline | Idle |

A state left empty falls back to Idle, so a component only defines what it needs. Two states
can share a style (Button's Selected is Segment's Selected).

**The state rules** keep the UI consistent:

- *At rest*, content panels (sections, list rows, the telemetry detail's well) are a dark,
  slightly see-through well with no light. Only something you can use lights up, and only
  on hover.
- *Hover* turns on a faint light from the bottom edge and firms the edge. It is kept quiet so
  it never looks like Selected.
- *Selected* takes an overexposed edge (near white, see Highlight below) and a wide glow in its
  colour. A selected or disabled control does not answer the pointer, except a component with
  a *Selected Hover* look: the focused telemetry card is lit like an alarm card, in the accent,
  and still answers the pointer.
- *Keyboard focus* shows the hover look plus the **Focus Ring**, a near-white outline a gap
  outside the control. It shows only while the keyboard is in use; moving or pressing the
  pointer hides it.
- A *Badge* is text alone, in the state's colour: no box, so it never reads as a button.
- *Warning* and *Critical* colour the edge and surface and breathe gently. They still answer the
  pointer, with a brighter edge and a higher light.
- *Unavailable* is grey with diagonal hatching.
- An icon glows only faintly, and only while its control is hovered.
- Glow and shadow fade faster than the body, so dimmed things lose their halo first.

## Building new UI

**GameObject > Digital Twin UI** creates a component ready to use: Panel, Section, Button, Icon
Button, Segmented Control, Badge, Card, List Row. Each comes with:

- a **UI Shape** naming its component;
- a **UI Control State** that sets the state from the control: hover and press from the
  pointer (a finger never hovers), Disabled from the Selectable, Selected from its Selected box
  or a Toggle, and Warning, Critical or Unavailable from its Tone;
- **auto layout**: layout groups and a content size fitter, so it sizes to its content and
  stacks like auto layout in a design tool, padded and spaced from the tokens;
- texts with a **UI Text Style**.

To make a component of your own, create a **Digital Twin > UI Component** asset and a **UI Shape
Style** per state, add it to the theme's Components list, and set it on a UI Shape. Set the
shape's State yourself, or add a UI Control State to the control.

A segmented control is a Panel holding Segments that are Toggles in a Toggle Group; the one
that is on shows Selected, with no code.

## How it works

`UIShape` is an `Image`, so Buttons, layouts, masks and scripts work with it unchanged. It shows,
first match wins: its own single **Style**; its **Component** in its **State**; or, for UI
authored with sprites, the theme's component and state for the sprite it shows. That last rule
lets older code that swaps sprites for a state (the mode switch, the view-mode control, the
telemetry cards) drive procedural looks with no change.

The shape's rect is its body: glow and shadow are drawn outside it, so layouts never allow for
an effect margin. Edges are anti-aliased per pixel and strokes never fall below one screen pixel,
so the UI holds at any size and pixel density. All boxes share one material and batch together;
each shape's look travels in its vertices, and the pulse runs on shader time. Changes of state
blend over the new style's transition.

**Glow** is drawn in three layers outside the edge: a hot core hugging it, a coloured glow, and
a wide haze that carries the colour furthest. A style's **Highlight** (0 to 1) overexposes the
look, as a light too bright for the camera: the stroke and the hot core run towards white while
the wider glow keeps the colour. Selected looks use about 0.6, alarm cards 0.4, pressed 0.3; rest
and hover looks use none. The bottom light keeps the tone's own colour, unwhitened.

**Viewer settings** (`DigitalTwinSettings`, set from the gear beside the Telemetry / Service
switch) reach every shape: *High contrast* makes visible surfaces near opaque and firms their
edges; *Reduce motion* drops pulses and blends. Both apply after a look is chosen, so styles
never need to know about them.

`UITextLegibility` gives on-screen text the shadowed preset of its font, including text built at
run time.

## Keyboard

`KeyboardNavigator` (on the EventSystem) gives the interface web-page keyboard use. Tab and
Shift+Tab move through every control, telemetry card and service row in the order the hierarchy
is built; the arrow keys move between controls and change sliders; Enter presses; Esc steps back
(closes the service detail, releases a focused card, drops the focus). With nothing focused, the
arrow keys and A, D turn the camera, W, S, + and - zoom, and R or Home returns to the opening
view. A modal sets `KeyboardNavigator.Scope` so Tab stays inside it. Something that is not a
Selectable joins in by implementing `IKeyboardTarget`.

## Tools

- **Tools > Digital Twin > UI Skin > Apply Procedural Skin** creates any missing design-system
  assets, turns every Image in SampleScene and the UI prefabs into a UIShape in place, and removes
  the room the sprites left for baked glow. Run it again after any setup tool that rebuilds a UI
  prefab, such as **Set Up Telemetry Detail Panel**.
- **Reset Design System To Defaults** rewrites every token, component and style from
  `UIDesignSystemDefaults`, the readable source of the defaults.
- **Open Design System Catalog** rebuilds the catalog scene from the live assets.
- **Compare With Sprite Skin** switches the theme's Procedural Shapes off and on.
- Batch: `UIDesignSystemCatalog.BuildAndCapture` renders the catalog to the image above;
  `ProceduralUISkinCapture.Run` (without `-quit`) checks the shapes, hover and alarm states, the
  service panel and both layouts, and saves captures to `Logs/ProceduralUISkin`.
