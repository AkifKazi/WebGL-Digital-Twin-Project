Shader "Digital Twin/Machine X-Ray"
{
    Properties
    {
        [Header(Body)][Space(4)]
        [HDR] _BaseColor            ("Body Tint", Color) = (0.05, 0.28, 0.60, 1)
        _FillOpacity                ("Fill Opacity", Range(0, 1)) = 0.022
        _FillFresnel                ("Grazing Thickness", Range(0, 8)) = 3.5
        _Opacity                    ("Material Opacity", Range(0, 2)) = 1

        [Header(Edges)][Space(4)]
        [HDR] _EdgeColor            ("Edge Color", Color) = (0.72, 0.93, 1, 1)
        _EdgeThreshold              ("Edge Angle Threshold", Range(0, 1)) = 0.75
        _EdgeWidth                  ("Edge Line Width (px)", Range(0.5, 8)) = 2.2
        _EdgeSoftness               ("Edge Angular Bleed", Range(0, 0.5)) = 0.02
        _EdgeIntensity              ("Edge Intensity", Range(0, 10)) = 1.3
        _CreaseIntensity            ("Crease Intensity", Range(0, 10)) = 0.7
        _CreaseSharpness            ("Crease Sharpness", Range(0.1, 20)) = 4

        [Header(Lighting)][Space(4)]
        _LightFloor                 ("Unlit Floor", Range(0, 2)) = 0.35
        _LightInfluence             ("Light Influence", Range(0, 3)) = 0.9
        _LightWrap                  ("Light Wrap", Range(0, 1)) = 0.45
        _AmbientInfluence           ("Ambient Influence", Range(0, 3)) = 0.4
        _Sheen                      ("Sheen", Range(0, 4)) = 0.25
        _SheenSharpness             ("Sheen Sharpness", Range(1, 128)) = 24

        [Header(Interior)][Space(4)]
        _BackFaceDim                ("Back Face Dim", Range(0, 1)) = 0.25

        [Header(Hover Isolation)][Space(4)]
        _FocusDim                   ("Focus Dim", Range(0, 1)) = 0
        _GhostFillScale             ("Ghost Fill Scale", Range(0, 1)) = 0.05
        _GhostEdgeScale             ("Ghost Edge Scale", Range(0, 1)) = 0.3

        [Header(Contours)][Space(4)]
        _ContourSpacing             ("Contour Spacing (m)", Range(0.05, 5)) = 0.5
        _ContourIntensity           ("Contour Intensity", Range(0, 3)) = 0

        [Header(Alarm Status)][Space(4)]
        [HDR] _StatusColor          ("Status Color", Color) = (1, 0.4, 0.14, 1)
        _StatusBlend                ("Status Blend", Range(0, 1)) = 0
        _StatusPulseSpeed           ("Status Pulse Speed", Range(0, 6)) = 1.2
        _StatusPulseAmount          ("Status Pulse Amount", Range(0, 6)) = 1.5

        [Header(Selection)][Space(4)]
        _SelectionBlend             ("Selection Blend", Range(0, 1)) = 0
        _SelectionBoost             ("Selection Boost", Range(0, 6)) = 1.6

        [Header(Cross Section)][Space(4)]
        _ClipX                      ("Clip X", Float) = 1.5
        _ClipEnabled                ("Clip Enabled", Float) = 0
        _ClipDirection              ("Clip Direction", Float) = 1
        _ClipEdgeWidth              ("Cut Edge Width", Range(0.001, 1)) = 0.06
        _ClipEdgeGlow               ("Cut Edge Glow", Range(0, 8)) = 2

        [Header(Reveal Sweep)][Space(4)]
        _RevealBandWidth            ("Reveal Band Width", Range(0.01, 3)) = 0.35
        _RevealBandIntensity        ("Reveal Band Intensity", Range(0, 10)) = 3

        [Header(Flicker)][Space(4)]
        _FlickerAmount              ("Flicker Amount", Range(0, 0.5)) = 0
        _FlickerSpeed               ("Flicker Speed", Range(0, 60)) = 18
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+10"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One
        ZWrite Off
        ZTest LEqual

        // Interior surfaces. URP draws SRPDefaultUnlit before UniversalForward,
        // so back faces land underneath the outer shell.
        Pass
        {
            Name "XRayInterior"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex MachineXRayVertex
            #pragma fragment MachineXRayFragment
            #pragma target 3.0
            #pragma multi_compile_instancing

            #define XRAY_BACKFACE 1
            #include "MachineXRay.hlsl"
            ENDHLSL
        }

        // Outer shell.
        Pass
        {
            Name "XRayShell"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex MachineXRayVertex
            #pragma fragment MachineXRayFragment
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "MachineXRay.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
