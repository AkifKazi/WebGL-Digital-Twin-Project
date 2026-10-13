#!/usr/bin/env python3
"""Exports the Unity UI design system to the website's data file.

Reads the real assets (tokens, components, one style per state) and writes
docs/design-system/data.js, so the site never drifts from Unity. No third-party
packages: Unity's YAML is parsed by the small reader below.

    python3 Tools/DesignSystemSite/export_design_system.py

Run it again whenever a token, component or style changes, then commit data.js.
"""
import base64
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DS = ROOT / "Assets/UI/Design System"
THEME = ROOT / "Assets/Resources/UI Theme.asset"
FONTS = ROOT / "Assets/UI/Fonts"
SPRITES = ROOT / "Assets/UI/Sprites"
SITE = ROOT / "docs/design-system"

STATE_FIELDS = ["idle", "hover", "pressed", "selected", "selectedHover", "disabled",
                "warning", "warningHover", "critical", "criticalHover", "unavailable"]
GROUP_ORDER = ["Containers", "Controls", "Telemetry", "Conversation", "Marks"]
ICONS = ["Arrow Left Icon", "Arrow Right Icon", "Ask Icon", "Close Icon", "Configure Icon", "Cross Section Icon",
         "Full Model Icon", "Pin Icon", "XRay Icon", "Zoom In Icon", "Zoom Out Icon"]


# ---- Unity YAML reader --------------------------------------------------------------

def scalar(text):
    text = text.strip()
    if text == "":
        return ""
    if text.startswith("{") and text.endswith("}"):
        out = {}
        for part in re.split(r",\s*(?=\w+:)", text[1:-1]):
            if ":" in part:
                key, value = part.split(":", 1)
                out[key.strip()] = scalar(value)
        return out
    if text[0] == "'" and text[-1] == "'":
        return text[1:-1].replace("''", "'")
    if text[0] == '"' and text[-1] == '"':
        return json.loads(text)
    try:
        return int(text)
    except ValueError:
        pass
    try:
        return float(text)
    except ValueError:
        return text


def parse(path):
    """Parses the body of a Unity MonoBehaviour asset into nested dicts and lists."""
    lines = path.read_text(encoding="utf-8").splitlines()
    start = next(i for i, line in enumerate(lines) if line.startswith("MonoBehaviour:")) + 1
    rows = [(len(l) - len(l.lstrip(" ")), l.strip()) for l in lines[start:] if l.strip()]

    def block(i, indent):
        # A list when the first row is a dash, else a map.
        if rows[i][1].startswith("- "):
            items = []
            while i < len(rows) and rows[i][0] == indent and rows[i][1].startswith("- "):
                text = rows[i][1][2:]
                if re.match(r"^[\w ]+:( |$)", text) and not text.startswith("{"):
                    # A map item: its first key is on the dash's line.
                    rows[i] = (indent + 2, text)
                    item, i = block(i, indent + 2)
                    items.append(item)
                else:
                    items.append(scalar(text))
                    i += 1
            return items, i
        out = {}
        while i < len(rows) and rows[i][0] == indent and not rows[i][1].startswith("- "):
            key, _, value = rows[i][1].partition(":")
            i += 1
            if value.strip() == "":
                if i < len(rows) and rows[i][0] > indent:
                    out[key], i = block(i, rows[i][0])
                elif i < len(rows) and rows[i][0] == indent and rows[i][1].startswith("- "):
                    out[key], i = block(i, indent)
                else:
                    out[key] = ""
            else:
                text = value
                # Unity folds long plain scalars onto more indented lines.
                while i < len(rows) and rows[i][0] > indent and not re.match(r"^[\w ]+:( |$)", rows[i][1]):
                    text += " " + rows[i][1]
                    i += 1
                out[key.strip()] = scalar(text)
        return out, i

    result, _ = block(0, rows[0][0])
    return result


def guid_map(folder):
    """guid -> asset path, for every .asset under folder."""
    out = {}
    for meta in folder.rglob("*.asset.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(), re.M)
        if match:
            out[match.group(1)] = Path(str(meta)[:-5])
    return out


# ---- export -----------------------------------------------------------------------------

def hex_of(color):
    channel = lambda v: max(0, min(255, round(v * 255)))
    return "{:02X}{:02X}{:02X}".format(channel(color["r"]), channel(color["g"]), channel(color["b"]))


def ref(value):
    return value.get("guid") if isinstance(value, dict) and value.get("fileID") else None


def color_ref(value):
    return {"token": value.get("token", ""), "alpha": value.get("alpha", 1),
            "custom": [value["custom"][c] for c in "rgba"] if value.get("custom") else [0, 0, 0, 0]}


def main():
    guids = guid_map(DS)
    fonts = {}
    for meta in FONTS.glob("*SDF.asset.meta"):
        guid = re.search(r"^guid: (\w+)", meta.read_text(), re.M).group(1)
        fonts[guid] = "SemiBold" if "SemiBold" in meta.name else "Medium"

    tokens_asset = parse(DS / "UI Tokens.asset")
    tokens = {
        "colors": [{"name": c["name"], "hex": hex_of(c["color"]), "use": c.get("use", "")}
                   for c in tokens_asset["colors"]],
        "spacing": [{"name": s["name"], "value": s["value"], "use": s.get("use", "")}
                    for s in tokens_asset["spacing"]],
        "textStyles": [{"name": t["name"], "font": fonts.get(ref(t["font"]), "Medium"), "size": t["size"],
                        "letterSpacing": t["letterSpacing"], "uppercase": bool(t["uppercase"]),
                        "color": color_ref(t["color"]), "use": t.get("use", "")}
                       for t in tokens_asset["textStyles"]],
        "fastMotion": tokens_asset["fastMotion"],
        "normalMotion": tokens_asset["normalMotion"],
    }

    theme = parse(THEME)
    styles = {}
    components = []
    for entry in theme["components"]:
        path = guids[ref(entry)]
        asset = parse(path)
        description = str(asset.get("description", ""))
        group, _, text = description.partition(". ")
        if group not in GROUP_ORDER:
            continue  # Parts (accent trails) are drawn inside other components.
        states = {}
        for field in STATE_FIELDS:
            guid = ref(asset.get(field))
            if not guid:
                continue
            style_path = guids[guid]
            style = parse(style_path)
            name = style_path.stem
            styles[name] = {
                "kind": style["kind"], "cornerRadius": style["cornerRadius"], "chamfer": style["chamfer"],
                "chamferCorners": style["chamferCorners"],
                "tail": style.get("tail", 0), "tailSize": style.get("tailSize", 0),
                "fill": color_ref(style["fill"]), "gradientStart": style["gradientStart"],
                "gradientEnd": style["gradientEnd"], "hatch": style["hatch"],
                "stroke": color_ref(style["stroke"]), "strokeWidth": style["strokeWidth"],
                "glow": color_ref(style["glow"]), "glowSize": style["glowSize"],
                "glowStrength": style["glowStrength"], "innerGlow": style["innerGlow"],
                "highlight": style["highlight"], "bottomLight": style["bottomLight"],
                "bottomLightHeight": style["bottomLightHeight"],
                "shadow": color_ref(style["shadow"]),
                "shadowOffset": [style["shadowOffset"]["x"], style["shadowOffset"]["y"]],
                "shadowSoftness": style["shadowSoftness"], "transition": style["transition"],
                "fadeIn": bool(style["fadeIn"]), "pulse": style["pulse"], "pulseSpeed": style["pulseSpeed"],
            }
            states[field] = name
        components.append({"name": path.stem, "group": group, "description": text, "states": states})
    components.sort(key=lambda c: GROUP_ORDER.index(c["group"]))

    # Icons travel inside data.js as data URIs: a page opened from disk (Drive, a zip) cannot
    # load a file into a WebGL texture, but it can load a data URI.
    icons = {name: "data:image/png;base64," + base64.b64encode((SPRITES / f"{name}.png").read_bytes()).decode()
             for name in ICONS}
    data = {"tokens": tokens, "components": components, "styles": styles, "groups": GROUP_ORDER,
            "icons": icons}
    SITE.mkdir(parents=True, exist_ok=True)
    (SITE / "data.js").write_text(
        "// Generated by Tools/DesignSystemSite/export_design_system.py. Do not edit by hand.\n"
        "window.DESIGN_SYSTEM = " + json.dumps(data, indent=1) + ";\n", encoding="utf-8")

    # Rajdhani (SIL Open Font License), embedded so the page also works offline and from disk.
    faces = []
    for weight, name in ((500, "Rajdhani-Medium.ttf"), (600, "Rajdhani-SemiBold.ttf")):
        blob = base64.b64encode((FONTS / name).read_bytes()).decode()
        faces.append("@font-face{font-family:'Rajdhani';font-weight:%d;font-display:swap;"
                     "src:url(data:font/ttf;base64,%s) format('truetype');}" % (weight, blob))
    (SITE / "fonts.css").write_text("/* Generated by export_design_system.py. */\n" + "\n".join(faces) + "\n")

    print(f"{len(components)} components, {len(styles)} styles, {len(tokens['colors'])} colours "
          f"-> {SITE / 'data.js'}")


if __name__ == "__main__":
    sys.exit(main())
