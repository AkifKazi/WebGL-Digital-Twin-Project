// Procedural UI shapes: panels, buttons, cards and lines drawn per pixel from
// a signed distance, so no nine-slice is involved and the look holds at any
// size and pixel density. Icons keep their authored sprite and get the same
// shadow and glow.
//
// One quad per shape, grown past the shape's rect by the reach of its glow and
// shadow, so a layout only ever sizes the shape's body. Layers of a box,
// bottom to top:
//
//   shadow        a dark copy of the shape, offset and optionally softened. Kept
//                 sharp, it keeps lines and edges readable on a bright
//                 background, where a glow (light added) cannot show.
//   outer glow    light added outside the edge in three layers: a hot core
//                 hugging the edge, a coloured glow, and a wide haze that
//                 carries the colour furthest. With a highlight (overexposure)
//                 the core and the stroke whiten, as a light too bright for
//                 the camera does, while the outer layers keep the colour.
//   fill          a colour with an optional linear gradient towards one side.
//   hatch         optional diagonal stripes (an unavailable card).
//   inner glow    light added inside the edge, fading inwards.
//   bottom light  a lit bottom edge and light rising from it, as if a lamp
//                 under the shape were on. Hover turns it on.
//   stroke        an inside stroke, never thinner than one screen pixel.
//
// An icon (a sprite shape) draws its sprite tinted, a sharp shadow of the
// sprite's own outline, and a soft glow around it.
//
// Everything a shape needs travels in its vertices (see UIShape.cs for the
// packing), so every box on a canvas shares this one material and batches.
// The pulse (a slow breathing of the glow) runs on _Time here: the mesh is
// only rebuilt when a shape's look actually changes.
//
// Line segments of a leader line also get the leader line's reveal: a segment
// is drawn only up to _RevealDistance along the line's path.
Shader "UI/Digital Twin/Shape"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _HatchSpacing ("Hatch Spacing (units)", Float) = 9

        // Leader line reveal: the same inputs as the reveal shader graph.
        _SegmentStartDistance ("Segment Start Distance", Float) = 0
        _SegmentLength ("Segment Length", Float) = 1
        _RevealDistance ("Reveal Distance", Float) = 1000000
        _ReverseDirection ("Reverse Direction", Float) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        // Premultiplied: glow and light add (alpha 0), everything else blends over.
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Derivatives for anti-aliasing, and ten interpolators.
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float4 uv0    : TEXCOORD0;
                float4 uv1    : TEXCOORD1;
                float4 uv2    : TEXCOORD2;
                float4 uv3    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 local  : TEXCOORD0; // position from the centre, half size
                float4 shape  : TEXCOORD1; // box: corner radius, stroke, glow size, shadow softness
                                           // icon: the sprite's UV rect (min, size)
                float4 extra  : TEXCOORD2; // shadow offset xy, then box: inner glow, gradient end
                                           //                      icon: glow size, shadow softness
                float4 anim   : TEXCOORD3; // hatch, glow multiplier, bottom light,
                                           // 1 for an icon, else minus the highlight
                float4 cut    : TEXCOORD4; // chamfer, chamfer corner mask, gradient direction, light height
                half4 fill    : TEXCOORD5;
                half4 stroke  : TEXCOORD6;
                half4 glow    : TEXCOORD7; // rgb premultiplied by strength and fade
                half4 shadow  : TEXCOORD8;
                float4 mask   : TEXCOORD9;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            float _HatchSpacing;
            float _SegmentStartDistance;
            float _SegmentLength;
            float _RevealDistance;
            float _ReverseDirection;

            // ---- unpacking (see UIShape.cs) --------------------------------

            // Three bytes in one float: exact, since a float holds 24-bit integers.
            float3 Unpack888(float v)
            {
                float c = floor(v / 65536.0);
                v -= c * 65536.0;
                float b = floor(v / 256.0);
                float a = v - b * 256.0;
                return float3(a, b, c);
            }

            // Two 12-bit values, each stored in sixteenths of a unit.
            float2 Unpack12(float v)
            {
                float hi = floor(v / 4096.0);
                return float2(v - hi * 4096.0, hi) / 16.0;
            }

            half3 UnpackColor(float v)
            {
                half3 c = Unpack888(v) / 255.0;
                // Packed colours are authored in sRGB, like any other UI colour.
                if (!IsGammaSpace())
                    c = UIGammaToLinear(c);
                return c;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                                  0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                half4 tint = v.color * _Color;

                float3 flags = Unpack888(v.uv3.w);              // kind + highlight * 2, chamfer, mask + direction * 16
                float kind = fmod(flags.x, 2.0);
                float highlight = floor(flags.x / 2.0) / 127.0;
                float3 alphas = Unpack888(v.uv2.y) / 255.0;     // fill, stroke, glow strength / 4
                float3 shadowPulse = Unpack888(v.uv3.y) / 255.0; // shadow alpha, pulse, pulse speed / 2.55

                OUT.local = v.uv0;
                if (kind > 0.5)
                {
                    // An icon: the sprite's UV rect as is; offset, glow and softness packed.
                    OUT.shape = v.uv1;
                    OUT.extra = float4(Unpack12(v.uv2.z) - 128.0, Unpack12(v.uv3.z));
                    OUT.stroke = 0;
                    OUT.cut = 0;
                    OUT.anim = float4(0, 1, 0, kind);
                }
                else
                {
                    float3 innerGradHatch = Unpack888(v.uv1.w) / 255.0;
                    OUT.shape = float4(Unpack12(v.uv1.x), Unpack12(v.uv1.y));
                    OUT.extra = float4(Unpack12(v.uv1.z) - 128.0, innerGradHatch.x, innerGradHatch.y * 2.0);
                    // Overexposed: the stroke runs towards white, keeping a trace of its colour.
                    half3 strokeColor = lerp(UnpackColor(v.uv2.z), 1.0, highlight * 0.8);
                    OUT.stroke = half4(strokeColor * tint.rgb, alphas.y * tint.a);
                    float2 light = Unpack12(v.uv3.z) / 100.0;   // strength, height as a share
                    OUT.anim = float4(innerGradHatch.z, 1.0, light.x * tint.a, -highlight);
                    OUT.cut = float4(flags.y, fmod(flags.z, 16.0), floor(flags.z / 16.0), light.y);
                }

                OUT.fill = half4(UnpackColor(v.uv2.x) * tint.rgb, alphas.x * tint.a);
                // Glow and shadow fade with the square of the tint's alpha: a dimmed
                // or fading shape (a peer card, a resting leader line) loses its halo
                // before its body, so faint lines do not leave a haze behind.
                float fade = tint.a * tint.a;
                float glowStrength = alphas.z * 4.0 * fade;
                OUT.glow = half4(UnpackColor(v.uv2.w) * tint.rgb * glowStrength, glowStrength);
                OUT.shadow = half4(UnpackColor(v.uv3.x), shadowPulse.x * fade);

                // The pulse breathes the glow; it is the same for the whole shape.
                float pulseSpeed = shadowPulse.z * 2.55;
                float breathe = 0.5 - 0.5 * cos(_Time.y * pulseSpeed * 6.2831853);
                OUT.anim.y = 1.0 + shadowPulse.y * 2.0 * breathe;
                return OUT;
            }

            // ---- distances -------------------------------------------------

            float SdBox(float2 p, float2 halfSize, float radius, float chamfer, float cornerMask)
            {
                radius = min(radius, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - halfSize + radius;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                if (chamfer > 0.0)
                {
                    // Corner bits: 1 top left, 2 top right, 4 bottom right, 8 bottom left.
                    float bit = p.y >= 0.0 ? (p.x < 0.0 ? 1.0 : 2.0) : (p.x >= 0.0 ? 4.0 : 8.0);
                    float on = fmod(floor(cornerMask / bit), 2.0);
                    chamfer = min(chamfer, min(halfSize.x, halfSize.y) * 2.0);
                    float cutDistance = (abs(p.x) + abs(p.y) - (halfSize.x + halfSize.y - chamfer)) * 0.70710678;
                    d = on > 0.5 ? max(d, cutDistance) : d;
                }
                return d;
            }

            // ---- icons -----------------------------------------------------

            // The sprite at a point given in its rect's 0-1 space; clear outside it.
            half4 Sprite(float2 at, float4 uvRect)
            {
                float2 inside = step(0.0, at) * step(at, 1.0);
                // Explicit level: icons are sampled inside a branch and around the sprite.
                half4 texel = tex2Dlod(_MainTex, float4(uvRect.xy + saturate(at) * uvRect.zw, 0.0, 0.0));
                return texel * (inside.x * inside.y);
            }

            fixed4 FragIcon(v2f IN)
            {
                float2 halfSize = max(IN.local.zw, 1e-3);
                float2 at = IN.local.xy / (halfSize * 2.0) + 0.5;
                float2 unit = 1.0 / (halfSize * 2.0);     // one canvas unit in rect space

                half4 texel = Sprite(at, IN.shape);
                half bodyAlpha = texel.a * IN.fill.a;
                half4 color = half4(texel.rgb * IN.fill.rgb * bodyAlpha, bodyAlpha);

                // A sharp shadow of the sprite's outline, under the sprite.
                float2 offset = IN.extra.xy * unit;
                half shadowAlpha = Sprite(at - offset, IN.shape).a * IN.shadow.a;
                if (IN.extra.w > 0.0)
                {
                    float2 spread = IN.extra.w * 0.5 * unit;
                    shadowAlpha = (shadowAlpha
                        + Sprite(at - offset + float2(spread.x, 0), IN.shape).a * IN.shadow.a
                        + Sprite(at - offset - float2(spread.x, 0), IN.shape).a * IN.shadow.a
                        + Sprite(at - offset + float2(0, spread.y), IN.shape).a * IN.shadow.a
                        + Sprite(at - offset - float2(0, spread.y), IN.shape).a * IN.shadow.a) * 0.2;
                }
                color += half4(IN.shadow.rgb, 1.0) * shadowAlpha * (1.0 - bodyAlpha);

                // A soft glow: the outline sampled on two rings around the point.
                if (IN.glow.a > 0.001)
                {
                    float2 reach = IN.extra.z * unit;
                    half sum = 0.0;
                    [unroll] for (int i = 0; i < 8; i++)
                    {
                        float angle = i * 0.78539816;
                        float2 direction = float2(cos(angle), sin(angle));
                        sum += Sprite(at + direction * reach * 0.4, IN.shape).a * 0.6;
                        sum += Sprite(at + direction * reach, IN.shape).a * 0.4;
                    }
                    half glow = saturate(sum / 8.0) * (1.0 - bodyAlpha);
                    color.rgb += IN.glow.rgb * glow * IN.anim.y;
                }
                return color;
            }

            // ---- boxes -----------------------------------------------------

            fixed4 FragBox(v2f IN, float pixel)
            {
                float2 p = IN.local.xy;
                float2 halfSize = IN.local.zw;
                float aa = pixel * 0.75;

                // A body thinner than a pixel (a leader line) is drawn a pixel wide
                // and correspondingly fainter, so it neither vanishes nor flickers.
                float2 drawnHalf = max(halfSize, pixel * 0.5);
                float thin = saturate(halfSize.x * 2.0 / pixel) * saturate(halfSize.y * 2.0 / pixel);

                float radius = IN.shape.x;
                float d = SdBox(p, drawnHalf, radius, IN.cut.x, round(IN.cut.y));
                float body = saturate(0.5 - d / aa * 0.5);
                float outside = max(d, 0.0);

                // Shadow: a sharp or softened offset copy, under everything.
                float softness = IN.shape.w;
                float ds = SdBox(p - IN.extra.xy, drawnHalf, radius, IN.cut.x, round(IN.cut.y));
                float shadowCover = 1.0 - smoothstep(-softness * 0.5 - aa, softness * 0.5 + aa, ds);
                half4 color = half4(IN.shadow.rgb, 1.0) * (IN.shadow.a * shadowCover * thin);

                // Outer glow: a hot core on the edge, a coloured glow, a wide haze.
                float glowSize = max(IN.shape.z, 1e-3);
                float highlight = -IN.anim.w;
                float outsideOnly = (1.0 - body) * step(1e-3, IN.shape.z);
                float hot = exp(-outside / max(glowSize * 0.06, 0.6));
                float core = exp(-outside / (glowSize * 0.2));
                float haze = pow(saturate(1.0 - outside / glowSize), 1.8);
                float glow = (hot * 0.25 + core * 0.45 + haze * 0.4) * outsideOnly;
                color.rgb += IN.glow.rgb * glow * IN.anim.y;
                // The overexposed core: glow.a is the glow's strength, so this
                // whitens in step with it.
                color.rgb += lerp(IN.glow.rgb, IN.glow.aaa, 0.75) * hot * highlight * 0.9 * outsideOnly * IN.anim.y;

                // Fill, with its gradient: 0 down, 1 up, 2 right, 3 left from full.
                float direction = round(IN.cut.z);
                float2 uv = p / max(halfSize, 1e-3) * 0.5 + 0.5;
                float along = direction < 0.5 ? 1.0 - uv.y : direction < 1.5 ? uv.y : direction < 2.5 ? uv.x : 1.0 - uv.x;
                half gradient = lerp(1.0, IN.extra.w, saturate(along));
                half fillAlpha = IN.fill.a * gradient * body * thin;
                half3 fillColor = IN.fill.rgb * lerp(1.0, gradient, 0.35);
                color = half4(fillColor * fillAlpha, fillAlpha) + color * (1.0 - fillAlpha);

                // Hatch: diagonal stripes in the stroke colour.
                if (IN.anim.x > 0.0)
                {
                    float stripe = frac((p.x + p.y) / _HatchSpacing);
                    float band = saturate((abs(stripe - 0.5) - 0.25) * _HatchSpacing / max(pixel, 1e-3) + 0.5);
                    half hatchAlpha = IN.anim.x * band * body * IN.stroke.a;
                    color = half4(IN.stroke.rgb * hatchAlpha, hatchAlpha) + color * (1.0 - hatchAlpha);
                }

                // Inner glow: light along the inside of the edge. Measured to a
                // well-rounded copy of the shape, so the corners blend instead of
                // showing a mitred seam.
                float innerReach = max(glowSize * 0.45, 2.0);
                float innerDistance = SdBox(p, drawnHalf, max(radius, innerReach * 1.5), 0.0, 0.0);
                float innerGlow = exp(-max(-innerDistance, 0.0) / innerReach) * body * IN.extra.z;
                half3 innerColor = lerp(IN.glow.rgb, IN.glow.aaa, highlight * 0.5);
                color.rgb += innerColor * innerGlow * IN.anim.y * thin;

                // Bottom light: a lit bottom edge and light rising from it, its
                // reach a share of the height. It is the stroke's colour, so it
                // reads as the edge itself lighting up.
                if (IN.anim.z > 0.0)
                {
                    float rise = max(p.y + drawnHalf.y, 0.0);
                    float height = max(IN.cut.w * drawnHalf.y * 2.0, 2.0);
                    float lamp = exp(-rise / (height * 0.35)) * 0.6 + pow(saturate(1.0 - rise / height), 2.0) * 0.4;
                    float edge = saturate(1.0 - rise / max(1.5, pixel * 1.5));
                    // The tone's own colour: the glow's, unwhitened, else the stroke's.
                    half3 lightColor = IN.glow.a > 0.001 ? IN.glow.rgb / IN.glow.a
                                     : IN.stroke.a > 0.0 ? IN.stroke.rgb : IN.fill.rgb;
                    color.rgb += lightColor * (lamp * 0.55 + edge * 0.6) * IN.anim.z * body * thin;
                }

                // Stroke, inside the edge.
                if (IN.shape.y > 0.0)
                {
                    float width = max(IN.shape.y, pixel);
                    float innerBody = saturate(0.5 - (d + width) / aa * 0.5);
                    half strokeAlpha = (body - innerBody) * IN.stroke.a * thin;
                    color = half4(IN.stroke.rgb * strokeAlpha, strokeAlpha) + color * (1.0 - strokeAlpha);
                }

                // Leader line reveal along the path, from the segment's far end.
                float u = saturate(p.x / max(halfSize.x * 2.0, 1e-3) + 0.5);
                u = lerp(u, 1.0 - u, _ReverseDirection);
                color *= step(_SegmentStartDistance + u * _SegmentLength, _RevealDistance);
                return color;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Size of a screen pixel in the shape's units, for anti-aliasing and
                // for keeping hairlines at least a pixel wide at any scale. Taken
                // before branching: derivatives are only defined outside it.
                float pixel = max(max(fwidth(IN.local.x), fwidth(IN.local.y)), 1e-4);
                half4 color;
                [branch] if (IN.anim.w > 0.5)
                    color = FragIcon(IN);
                else
                    color = FragBox(IN, pixel);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
