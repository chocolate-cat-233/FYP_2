using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace SuperFinalFuse.RevealDisc
{
    public sealed class RevealDiscRenderFeature : ScriptableRendererFeature
    {
        [Header("Player")]
        [SerializeField]
        private LayerMask playerLayer = -1;

        [Header("Materials")]
        [SerializeField]
        private Material stencilWriteMaterial;
        [SerializeField]
        private Material groundDecalMaterial;
        [SerializeField]
        private Material playerRevealMaterial;

        private RevealDiscRenderPass renderPass;

        public override void Create()
        {
            renderPass = new RevealDiscRenderPass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer,
                                             ref RenderingData renderingData)
        {
            if (stencilWriteMaterial == null || playerRevealMaterial == null || playerLayer == 0)
                return;

            renderPass.stencilWriteMaterial = stencilWriteMaterial;
            renderPass.groundDecalMaterial  = groundDecalMaterial;
            renderPass.playerRevealMaterial = playerRevealMaterial;
            renderPass.playerLayer          = playerLayer;

            renderer.EnqueuePass(renderPass);
        }

        protected override void Dispose(bool disposing)
        {
            renderPass?.Dispose();
            renderPass = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Render Pass
        // ═══════════════════════════════════════════════════════════════

        private sealed class RevealDiscRenderPass : ScriptableRenderPass
        {
            internal Material   stencilWriteMaterial;
            internal Material   groundDecalMaterial;
            internal Material   playerRevealMaterial;
            internal LayerMask  playerLayer;

            private Mesh              discMesh;
            private Renderer[]        cachedPlayerRenderers;
            private int               cacheFrame = -1;

            private static readonly int PropActive = Shader.PropertyToID("_RevealDiscActive");
            private static readonly int PropCenter = Shader.PropertyToID("_RevealDiscCenter");
            private static readonly int PropRadius = Shader.PropertyToID("_RevealDiscRadius");

            private class PassData
            {
                public Material   stencilWriteMat;
                public Material   groundDecalMat;
                public Material   playerRevealMat;
                public Vector3    center;
                public float      radius;
                public Mesh       discMesh;
                public Renderer[] playerRenderers;
            }

            internal RevealDiscRenderPass()
            {
                profilingSampler = new ProfilingSampler(nameof(RevealDiscRenderPass));
            }

            internal void Dispose()
            {
                discMesh = null;
                cachedPlayerRenderers = null;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph,
                                                   ContextContainer frameData)
            {
                float active = Shader.GetGlobalFloat(PropActive);
                if (active < 0.5f) return;

                Vector3 center = Shader.GetGlobalVector(PropCenter);
                float   radius = Shader.GetGlobalFloat(PropRadius);
                if (radius <= 0.001f) return;

                if (discMesh == null)
                    discMesh = RevealDiscMesh.GetDiscMesh();

                // Refresh cached player renderers every 30 frames
                if (cacheFrame != Time.frameCount || cachedPlayerRenderers == null)
                {
                    cacheFrame = Time.frameCount;
                    var all = Object.FindObjectsByType<Renderer>(
                        FindObjectsSortMode.None);
                    var filtered = new System.Collections.Generic.List<Renderer>();
                    int mask = playerLayer.value;
                    foreach (var r in all)
                    {
                        if (r != null && (mask & (1 << r.gameObject.layer)) != 0)
                            filtered.Add(r);
                    }
                    cachedPlayerRenderers = filtered.ToArray();
                }

                using (var builder = renderGraph.AddUnsafePass<PassData>(
                    "RevealDisc", out var passData))
                {
                    passData.stencilWriteMat  = stencilWriteMaterial;
                    passData.groundDecalMat   = groundDecalMaterial;
                    passData.playerRevealMat  = playerRevealMaterial;
                    passData.center           = center;
                    passData.radius           = radius;
                    passData.discMesh         = discMesh;
                    passData.playerRenderers  = cachedPlayerRenderers;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(
                        (PassData data, UnsafeGraphContext context) =>
                        {
                            var cmd = context.cmd;

                            // ── Step 1: Stencil Write (fullscreen) ──
                            cmd.DrawProcedural(Matrix4x4.identity,
                                               data.stencilWriteMat, 0,
                                               MeshTopology.Triangles, 3, 1);

                            // ── Step 2: Ground Decal ──────────────────
                            if (data.groundDecalMat != null)
                            {
                                var m = Matrix4x4.TRS(data.center,
                                    Quaternion.identity,
                                    new Vector3(data.radius, 1f, data.radius));
                                cmd.DrawMesh(data.discMesh, m,
                                             data.groundDecalMat, 0, 0);
                            }

                            // ── Step 3: Player Reveal ─────────────────
                            if (data.playerRenderers != null)
                            {
                                foreach (var r in data.playerRenderers)
                                {
                                    if (r == null || !r.enabled) continue;
                                    var mats = r.sharedMaterials;
                                    for (int i = 0; i < mats.Length; i++)
                                    {
                                        cmd.DrawRenderer(r,
                                            data.playerRevealMat, i, 0);
                                    }
                                }
                            }
                        });
                }
            }

            [System.Obsolete]
            public override void Execute(ScriptableRenderContext context,
                                         ref RenderingData renderingData) { }
        }
    }
}
