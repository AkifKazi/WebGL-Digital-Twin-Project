// The service view's run bars: the default UI shader, plus the fill drawn per
// pixel. The fill brightens from its start to its leading end, where a hot
// core whitens it; a soft glow surrounds it, growing towards the lead, and
// falls off smoothly all round, so its ends and corners stay round rather
// than showing the edges of the quads it is drawn on. A bright pulse travels
// along the fill into the lead, swelling the glow as it passes. The fill is
// already brightest at its lead, so the pulse only ever adds light.
//
// The fill is one quad, its glow's reach larger than the fill on every side.
// Its vertices carry, in UV0, the position in units from the fill's start
// and its centre line; in UV1 the fill's length and half height; in UV2 the
// glow's reach and 1 plus the bar's pulse phase. Anything else (track, ticks,
// hatching) has UV2 zero and is drawn as plain UI. The canvas must pass UV1
// and UV2 (UIServiceBarGraphic turns them on).
Shader "UI/Service Run Bar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PulseSeconds ("Pulse Period (s)", Float) = 3.4
        _PulseTravel ("Share Of The Period Spent Travelling", Range(0.2, 1)) = 0.65
        _PulseWidth ("Pulse Width (share of the fill)", Range(0.02, 0.5)) = 0.14
        _PulseStrength ("Pulse Strength", Range(0, 2)) = 0.7

        _StartBrightness ("Fill Start Brightness", Range(0, 1)) = 0.4
        _CoreWhite ("Hot Core At The Lead", Range(0, 1)) = 0.2
        _CornerRadius ("Fill Corner Radius", Range(0, 4)) = 1.5
        _GlowStrength ("Glow Strength", Range(0, 1.5)) = 0.26
        _GlowStart ("Glow At The Fill's Start (share of the lead's)", Range(0, 1)) = 0.12

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Derivatives, for the fill's anti-aliased edge.
            #pragma target 2.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 size     : TEXCOORD1;
                float2 extra    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask          : TEXCOORD2;
                float2 size          : TEXCOORD3;
                float2 extra         : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            float _PulseSeconds;
            float _PulseTravel;
            float _PulseWidth;
            float _PulseStrength;
            float _StartBrightness;
            float _CoreWhite;
            float _CornerRadius;
            float _GlowStrength;
            float _GlowStart;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = v.texcoord;
                OUT.size = v.size;
                OUT.extra = v.extra;
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                                  0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    v.color.rgb = UIGammaToLinear(v.color.rgb);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                // White texture unless a sprite is set: sample its centre so
                // the pulse coordinates never pick up texture detail.
                half4 color = IN.color * (tex2D(_MainTex, float2(0.5, 0.5)) + _TextureSampleAdd);

                // Premultiplied below for plain UI; the fill is premultiplied as it is built.
                bool premultiplied = false;
                if (IN.extra.y >= 1.0)
                {
                    float len = max(IN.size.x, 0.001);
                    float halfHeight = max(IN.size.y, 0.5);
                    float reach = max(IN.extra.x, 0.001);
                    float2 p = IN.texcoord;
                    float t = saturate(p.x / len);

                    float phase = frac(_Time.y / max(_PulseSeconds, 0.1) + frac(IN.extra.y));
                    float centre = phase / max(_PulseTravel, 0.01);
                    float spread = (t - centre) / max(_PulseWidth, 0.001);
                    float pulse = exp(-spread * spread) * _PulseStrength;

                    // Signed distance to the fill, a box with slightly rounded corners.
                    float corner = min(halfHeight, _CornerRadius);
                    float2 q = abs(float2(p.x - len * 0.5, p.y)) - float2(len * 0.5, halfHeight) + corner;
                    float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - corner;
                    float body = saturate(0.5 - d / max(fwidth(d), 0.0001));

                    half3 tint = IN.color.rgb;
                    half3 fill = tint * lerp(_StartBrightness, 1.0, t * t) * (1.0 + pulse);
                    float core = smoothstep(0.6, 1.0, t) * saturate(1.0 - abs(p.y) / halfHeight);
                    fill = min(lerp(fill, 1.0, core * _CoreWhite), 1.0);

                    // A Gaussian of the distance outside the fill: no edge anywhere.
                    float outside = max(d, 0.0);
                    float sigma = reach * 0.36;
                    float falloff = exp(-outside * outside / (2.0 * sigma * sigma));
                    float along = lerp(_GlowStart, 1.0, smoothstep(0.0, 1.0, t));
                    float glow = saturate(falloff * along * _GlowStrength * (1.0 + pulse)) * (1.0 - body);

                    half bodyAlpha = body * color.a;
                    half glowAlpha = glow * color.a;
                    color.rgb = fill * bodyAlpha + tint * glowAlpha;
                    color.a = bodyAlpha + glowAlpha;
                    premultiplied = true;
                }

                if (!premultiplied)
                    color.rgb *= color.a;

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
