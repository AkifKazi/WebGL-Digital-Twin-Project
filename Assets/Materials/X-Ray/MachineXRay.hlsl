#ifndef MACHINE_XRAY_INCLUDED
#define MACHINE_XRAY_INCLUDED

// Shared body for the two passes of "Digital Twin/Machine X-Ray".
// The interior pass defines XRAY_BACKFACE; the shell pass does not.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4  _BaseColor;
    half4  _EdgeColor;
    half4  _StatusColor;

    half   _FillOpacity;
    half   _FillFresnel;
    half   _Opacity;

    half   _EdgeThreshold;
    half   _EdgeWidth;
    half   _EdgeSoftness;
    half   _EdgeIntensity;
    half   _CreaseIntensity;
    half   _CreaseSharpness;

    half   _LightFloor;
    half   _LightInfluence;
    half   _LightWrap;
    half   _AmbientInfluence;
    half   _Sheen;
    half   _SheenSharpness;

    half   _BackFaceDim;

    half   _FocusDim;
    half   _GhostFillScale;
    half   _GhostEdgeScale;

    half   _ContourSpacing;
    half   _ContourIntensity;

    half   _StatusBlend;
    half   _StatusPulseSpeed;
    half   _StatusPulseAmount;

    half   _SelectionBlend;
    half   _SelectionBoost;

    half   _FlickerAmount;
    half   _FlickerSpeed;

    float  _ClipX;
    float  _ClipEnabled;
    float  _ClipDirection;
    half   _ClipEdgeWidth;
    half   _ClipEdgeGlow;

    half   _RevealBandWidth;
    half   _RevealBandIntensity;
CBUFFER_END

// Globals driven by MachineXRayViewController via Shader.SetGlobalFloat.
float _XRayOpacity;
float _XRayReveal;
float _XRayRevealMinY;
float _XRayRevealMaxY;
float _XRayRevealActive;
float _XRayScanY;
float _XRayScanWidth;
float _XRayScanIntensity;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3  normalWS   : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings MachineXRayVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

    output.positionCS = positions.positionCS;
    output.positionWS = positions.positionWS;
    output.normalWS = normals.normalWS;

    return output;
}

// Anti-aliased line at every integer step of "coordinate".
half MachineXRayLine(float coordinate)
{
    float distanceToLine = abs(frac(coordinate + 0.5) - 0.5);
    float width = fwidth(coordinate) * 1.5 + 1e-5;
    return 1.0h - smoothstep(0.0, width, distanceToLine);
}

half4 MachineXRayFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 positionWS = input.positionWS;

    // Cross-section clipping, compatible with HybridHopperClipController.
    float clipSide = (_ClipX - positionWS.x) * _ClipDirection;

    if (_ClipEnabled > 0.5 && clipSide < 0.0)
        discard;

    // Build-up sweep used when entering the view.
    float revealY = lerp(_XRayRevealMinY, _XRayRevealMaxY, saturate(_XRayReveal));

    if (_XRayRevealActive > 0.5 && positionWS.y > revealY)
        discard;

    // Hard mesh edges are found from the screen-space change of the raw
    // interpolated normal: neighbouring triangles disagree across a crease,
    // while smooth curvature changes only gradually.
    half3 rawNormal = input.normalWS;
    half creaseAmount = saturate(length(fwidth(rawNormal)) * _CreaseSharpness);

    half3 normalWS = normalize(rawNormal);
#if defined(XRAY_BACKFACE)
    normalWS = -normalWS;
#endif

    half3 viewWS = normalize(GetCameraPositionWS() - positionWS);
    half fresnel = 1.0h - saturate(dot(normalWS, viewWS));

    // Directional lighting keeps upward faces brighter than vertical ones, so
    // the shell still reads as a solid object rather than a flat overlay.
    Light mainLight = GetMainLight();
    half wrapped = saturate((dot(normalWS, mainLight.direction) + _LightWrap) / (1.0h + _LightWrap));
    half3 ambient = SampleSH(normalWS);

    half3 halfVector = normalize(mainLight.direction + viewWS);
    half sheen = pow(saturate(dot(normalWS, halfVector)), max(_SheenSharpness, 1.0h)) * _Sheen;

    half lightLevel = _LightFloor + _LightInfluence * wrapped;

    // Alarm state recolours the part: blue nominal, amber warning, red critical.
    half statusBlend = saturate(_StatusBlend);
    half3 bodyColor = lerp(_BaseColor.rgb, _StatusColor.rgb, statusBlend);
    half3 edgeColor = lerp(_EdgeColor.rgb, _StatusColor.rgb, statusBlend * 0.85h);

    // Face-on surfaces stay nearly invisible so interior parts read through;
    // grazing surfaces thicken into the glass shell.
    half fillAmount = _FillOpacity * (1.0h + _FillFresnel * fresnel * fresnel);
    half3 fillPart = bodyColor * (lightLevel + ambient * _AmbientInfluence) * fillAmount;
    fillPart += edgeColor * sheen * fillAmount;

    half3 linePart = 0.0h;

    // Silhouette line. Dividing by the screen-space rate of change of the
    // facing ratio keeps a constant pixel weight instead of smearing the band
    // across gently curved surfaces.
    half insideAmount = _EdgeThreshold - fresnel;
    half edgeWidth = fwidth(fresnel) * _EdgeWidth + _EdgeSoftness;
    half edgeMask = 1.0h - smoothstep(0.0h, max(edgeWidth, 1e-5h), insideAmount);
    linePart += edgeColor * edgeMask * _EdgeIntensity;
    linePart += edgeColor * creaseAmount * _CreaseIntensity;

    if (_ContourIntensity > 0.0h)
    {
        half contour = MachineXRayLine(positionWS.y / max(_ContourSpacing, 0.001h));
        linePart += edgeColor * contour * _ContourIntensity * (0.35h + 0.65h * fresnel);
    }

    if (_ClipEnabled > 0.5 && _ClipEdgeGlow > 0.0h)
    {
        half nearCut = saturate(1.0h - clipSide / max(_ClipEdgeWidth, 0.001h));
        linePart += edgeColor * nearCut * nearCut * _ClipEdgeGlow;
    }

    if (_XRayScanIntensity > 0.0)
    {
        half scan = saturate(1.0 - abs(positionWS.y - _XRayScanY) / max(_XRayScanWidth, 0.001));
        linePart += edgeColor * scan * scan * scan * _XRayScanIntensity;
    }

    if (_XRayRevealActive > 0.5)
    {
        half band = saturate(1.0 - (revealY - positionWS.y) / max(_RevealBandWidth, 0.001h));
        linePart += edgeColor * band * band * _RevealBandIntensity;
    }

    // Warning and critical parts pulse so they are found without hunting.
    if (statusBlend > 0.0h)
    {
        half pulse = 0.5h + 0.5h * sin(_Time.y * _StatusPulseSpeed * 6.2831853h);
        linePart += _StatusColor.rgb * statusBlend * _StatusPulseAmount * pulse * (edgeMask + fillAmount);
    }

    // Selection highlight for inspecting one mechanism at a time.
    if (_SelectionBlend > 0.0h)
        linePart += edgeColor * _SelectionBlend * _SelectionBoost * (edgeMask + creaseAmount + fillAmount);

    // Hover isolation: the body fades out much faster than the outline, so a
    // de-focused mechanism survives as a faint wireframe and the machine keeps
    // its shape while one part is inspected.
    half focusDim = saturate(_FocusDim);
    half3 color = fillPart * lerp(1.0h, _GhostFillScale, focusDim)
                + linePart * lerp(1.0h, _GhostEdgeScale, focusDim);

    if (_FlickerAmount > 0.0h)
    {
        half flicker = 1.0h - _FlickerAmount * (0.5h + 0.5h * sin(_Time.y * _FlickerSpeed + positionWS.y * 24.0h));
        color *= flicker;
    }

#if defined(XRAY_BACKFACE)
    color *= _BackFaceDim;
#endif

    color *= _Opacity * _XRayOpacity;

    // Additive output: order independent, so overlapping shells never sort
    // against each other and no depth sorting pass is needed.
    return half4(color, 1.0h);
}

#endif
