using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Editor-style selection outline. The marked renderers are drawn into a
/// coverage mask with depth ignored, then the mask's edge is traced as an
/// outline and its interior tinted faintly. Ignoring depth is what lets a part
/// hidden inside a housing still be outlined, and what makes a mechanism built
/// from many meshes read as a single silhouette.
///
/// Nothing is queued while nothing is selected, so idle cost is zero.
/// </summary>
public sealed class SelectionOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        [Tooltip("Outline thickness in pixels on a 1080-pixel-tall screen. Scaled with resolution so it reads the same on phones and monitors.")]
        [Range(0.5f, 8f)] public float outlineWidth = 2f;

        [Tooltip("Opacity of the outline at full selection strength.")]
        [Range(0f, 1f)] public float outlineOpacity = 1f;

        [Tooltip("Opacity of the tint laid over the selected part, including the portions hidden behind other geometry.")]
        [Range(0f, 1f)] public float fillOpacity = 0.18f;
    }

    [SerializeField] private Settings settings = new();

    [Tooltip("Mask and composite shader. Assigned by scene setup so builds include it.")]
    [SerializeField] private Shader shader;

    private Material material;
    private Material clippedMaskMaterial;
    private MaskPass maskPass;
    private CompositePass compositePass;

    public override void Create()
    {
        if (shader == null)
            shader = Shader.Find("Hidden/Digital Twin/Selection Outline");

        if (shader == null)
            return;

        material = CoreUtils.CreateEngineMaterial(shader);
        material.SetFloat(MaskPass.ClipEnabledId, 0f);

        // Parts cut by the section clip are masked with their own clip, so
        // the Cross Section view outlines only what is left of them.
        clippedMaskMaterial = CoreUtils.CreateEngineMaterial(shader);
        clippedMaskMaterial.SetFloat(MaskPass.ClipEnabledId, 1f);

        // The mask is drawn while the camera matrices are still the scene's;
        // the composite runs after post-processing so the outline keeps its
        // exact colour instead of being tonemapped.
        maskPass = new MaskPass(material, clippedMaskMaterial) { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        compositePass = new CompositePass(material, settings) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null || !SelectionOutline.IsActive)
            return;

        renderer.EnqueuePass(maskPass);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        CoreUtils.Destroy(clippedMaskMaterial);
        material = null;
        clippedMaskMaterial = null;
    }

    /// <summary>Hands the mask from the first pass to the second within a frame.</summary>
    private sealed class OutlineFrameData : ContextItem
    {
        public TextureHandle mask;

        public override void Reset() => mask = TextureHandle.nullHandle;
    }

    private sealed class MaskPass : ScriptableRenderPass
    {
        public static readonly int ClipXId = Shader.PropertyToID("_ClipX");
        public static readonly int ClipEnabledId = Shader.PropertyToID("_ClipEnabled");

        private sealed class PassData
        {
            public List<Material> materials;
            public List<Renderer> renderers;
            public List<int> submeshCounts;
        }

        private readonly Material material;
        private readonly Material clippedMaterial;
        private readonly GraphicsFormat maskFormat;
        private readonly List<Renderer> visible = new();
        private readonly List<Material> materials = new();
        private readonly List<int> submeshCounts = new();
        private readonly MaterialPropertyBlock block = new();

        public MaskPass(Material material, Material clippedMaterial)
        {
            this.material = material;
            this.clippedMaterial = clippedMaterial;

            // Single-channel where supported; WebGL 2 supports R8 render targets.
            maskFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render)
                ? GraphicsFormat.R8_UNorm
                : GraphicsFormat.R8G8B8A8_UNorm;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Only the player's view; scene view and previews keep their own selection.
            if (cameraData.cameraType != CameraType.Game)
                return;

            visible.Clear();
            materials.Clear();
            submeshCounts.Clear();

            foreach (Renderer renderer in SelectionOutline.Targets)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                // The section clip lives in each renderer's property block. Every
                // clipped part shares the one clip position, so one material serves.
                renderer.GetPropertyBlock(block);
                bool clipped = block.GetFloat(ClipEnabledId) > 0.5f;

                if (clipped)
                    clippedMaterial.SetFloat(ClipXId, block.GetFloat(ClipXId));

                visible.Add(renderer);
                materials.Add(clipped ? clippedMaterial : material);
                submeshCounts.Add(Mathf.Max(1, renderer.sharedMaterials.Length));
            }

            if (visible.Count == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            TextureDesc desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
            desc.name = "_SelectionOutlineMask";
            desc.format = maskFormat;
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.filterMode = FilterMode.Bilinear;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;

            TextureHandle mask = renderGraph.CreateTexture(desc);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Selection Outline Mask", out PassData data))
            {
                data.materials = materials;
                data.renderers = visible;
                data.submeshCounts = submeshCounts;

                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    for (int i = 0; i < d.renderers.Count; i++)
                    {
                        for (int s = 0; s < d.submeshCounts[i]; s++)
                            context.cmd.DrawRenderer(d.renderers[i], d.materials[i], s, 0);
                    }
                });
            }

            frameData.GetOrCreate<OutlineFrameData>().mask = mask;
        }
    }

    private sealed class CompositePass : ScriptableRenderPass
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int RadiusId = Shader.PropertyToID("_OutlineRadius");

        private sealed class PassData
        {
            public Material material;
            public TextureHandle mask;
        }

        private readonly Material material;
        private readonly Settings settings;

        public CompositePass(Material material, Settings settings)
        {
            this.material = material;
            this.settings = settings;

            // Drawing after post-processing needs a colour texture to blend into.
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!frameData.Contains<OutlineFrameData>())
                return;

            TextureHandle mask = frameData.Get<OutlineFrameData>().mask;

            if (!mask.IsValid())
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureDesc desc = renderGraph.GetTextureDesc(mask);

            int width = desc.width > 0 ? desc.width : cameraData.cameraTargetDescriptor.width;
            int height = desc.height > 0 ? desc.height : cameraData.cameraTargetDescriptor.height;

            float pixels = settings.outlineWidth * Mathf.Max(1f, height / 1080f);

            Color outline = SelectionOutline.OutlineColor;
            outline.a = settings.outlineOpacity * SelectionOutline.Strength;

            Color fill = SelectionOutline.OutlineColor;
            fill.a = settings.fillOpacity * SelectionOutline.Strength;

            material.SetColor(OutlineColorId, outline);
            material.SetColor(FillColorId, fill);
            material.SetVector(RadiusId, new Vector4(pixels / Mathf.Max(1, width), pixels / Mathf.Max(1, height), 0f, 0f));

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Selection Outline Composite", out PassData data))
            {
                data.material = material;
                data.mask = mask;

                builder.UseTexture(mask, AccessFlags.Read);

                // ReadWrite keeps the existing image: the outline is blended over it.
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, d.mask, new Vector4(1f, 1f, 0f, 0f), d.material, 1));
            }
        }
    }
}
