// GrassGeometryShared.hlsl
// Shared geometry-shader code for Custom/GrassGeometry. Each input
// triangle emits the ground surface plus up to _MaxBladesPerTriangle
// grass blades. Colors-only cartoon (toon) style — no textures.
// Vertex colors:
//   R — grass mask (blade density + ground blend)
//   G — wind scale (0 pins a spot down)
//   B — blade height multiplier (0 short, 1 tall)
//
// At most 20 blades per triangle (D3D11 geometry-shader output budget).
//
// Requires Shader Model 4.0+ (DX11/DX12/Vulkan). Metal and OpenGL ES
// fall back to Custom/GrassVertexColor (ground only).

#ifndef GRASS_GEOMETRY_SHARED_INCLUDED
#define GRASS_GEOMETRY_SHARED_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// ── Per-material properties ────────────────────────────────────────
CBUFFER_START(UnityPerMaterial)
    float4 _GrassColor;
    float4 _GroundColor;
    float4 _BladeTipColor;
    float  _BlendSoftness;
    float  _Density;
    float  _BladeHeight;
    float  _BladeWidth;
    float  _MaxBladesPerTriangle;
    float  _Billboard;
    float  _AlphaCutoff;
    float  _ShadowThreshold;
    float  _ShadowSoftness;
    float4 _AmbientColor;
    float  _AmbientStrength;
    float  _WindStrength;
    float  _WindFrequency;
    float  _WindSpeed;
    float4 _WindDirection;
CBUFFER_END

#if defined(GRASS_SHADOW_PASS)
float3 _LightDirection;
float3 _LightPosition;
#endif

// ── Global arrays set by GrassPushManager (collider interaction) ────
// xyz = world-space push centre, w = influence radius
float4 _GrassPushers[16];
float  _GrassPushStrengths[16];

// ── Deterministic per-blade random ──────────────────────────────────
// (Named uniquely — URP's Shadows.hlsl already defines a Hash function.)
uint GrassHash(uint seed)
{
    seed = seed ^ (seed >> 16);
    seed = seed * 0x7feb352du;
    seed = seed ^ (seed >> 15);
    seed = seed * 0x846ca68bu;
    seed = seed ^ (seed >> 16);
    return seed;
}

float Rand01(uint seed)
{
    return GrassHash(seed) * (1.0 / 4294967295.0);
}

// ── Vertex stage ────────────────────────────────────────────────────
struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;
};

struct GSIn
{
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float4 vertexColor : TEXCOORD2;
};

GSIn vert(Attributes IN)
{
    GSIn OUT;
    OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
    OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
    OUT.vertexColor = IN.color;
    return OUT;
}

// ── Geometry stage ──────────────────────────────────────────────────
// 8 scalar outputs (4 position + 3 world position + 1 packed flag) so the
// D3D11 limit maxvertexcount × output scalars ≤ 1024 allows 20 blades per
// triangle. Normals are reconstructed in the fragment via ddx/ddy.
struct GSOut
{
    float4 positionHCS : SV_POSITION;
    float3 positionWS  : TEXCOORD0;
    float  packed      : TEXCOORD1; // blade: 0..1 height; ground: -1 - mask
};

void EmitVertex(inout TriangleStream<GSOut> stream,
    float3 positionWS, float3 normalWS, float4 vertexColor, float bladeT)
{
    GSOut OUT;
    // Ground and blades share one scalar: blades carry their height 0..1,
    // ground carries the R mask encoded as -1 - mask so the two never
    // collide.
    OUT.packed = bladeT < 0.0
        ? -1.0 - saturate(vertexColor.r)
        : bladeT;
    OUT.positionWS = positionWS;

    float3 positionCS = positionWS;
#if defined(GRASS_SHADOW_PASS)
    positionCS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
#endif
    OUT.positionHCS = TransformWorldToHClip(positionCS);
#if defined(GRASS_SHADOW_PASS)
#if UNITY_REVERSED_Z
    OUT.positionHCS.z = min(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    OUT.positionHCS.z = max(OUT.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
#endif
    stream.Append(OUT);
}

void EmitBlade(inout TriangleStream<GSOut> stream,
    float3 base, float4 vertexColor, float mask, uint seed)
{
    float rot = Rand01(seed + 2u) * 6.2831853;
    float heightR = Rand01(seed + 3u);
    float leanR = Rand01(seed + 4u);
    float phase = Rand01(seed + 5u) * 6.2831853;

    // Blade facing: 0 = random 3D orientation, 1 = camera billboard
    // (blade plane perpendicular to the view direction, standing up).
    float2 dirXZ = float2(cos(rot), sin(rot));
    float3 toCamera = normalize(_WorldSpaceCameraPos - base);
    float2 viewXZ = normalize(toCamera.xz + float2(1e-4, 0.0));
    dirXZ = normalize(lerp(dirXZ, viewXZ, saturate(_Billboard)));
    float3 widthDir = float3(dirXZ.y, 0.0, -dirXZ.x);
    float lean = 0.15 + 0.35 * leanR;
    float height = _BladeHeight * (0.5 + 0.5 * heightR) *
        (0.45 + 0.55 * mask) * (0.5 + 0.7 * saturate(vertexColor.b));
    float width = _BladeWidth;
    float windScale = mask * saturate(vertexColor.g);

    float2 windXZ = normalize(_WindDirection.xz + float2(1e-4, 0.0));
    float3 windVec = float3(windXZ.x, 0.0, windXZ.y);
    float windPhase = phase +
        dot(base.xz, windXZ) * _WindFrequency;

    // Collider interaction: bend blades away from GrassPusher colliders.
    // The roots stay anchored; only the upper part of the blade moves.
    float push = 0.0;
    float3 pushVec = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < 16; i++)
    {
        float strength = _GrassPushStrengths[i];
        if (strength <= 0.0)
        {
            continue;
        }

        float2 delta = base.xz - _GrassPushers[i].xz;
        float dist = length(delta);
        float radius = max(_GrassPushers[i].w, 0.001);
        if (dist >= radius)
        {
            continue;
        }

        float falloff = 1.0 - dist / radius;
        float weight = strength * falloff * falloff;
        push += weight;
        pushVec += float3(delta.x, 0.0, delta.y) *
            (weight / max(dist, 0.001));
    }

    float3 left = base - widthDir * (width * 0.5);
    float3 right = base + widthDir * (width * 0.5);

    // Two segments so the wind bends the blade instead of just leaning it.
    // Strip L0 R0 L1 R1 L2 R2 = 6 verts, 2 quads — keeps the output
    // budget small under the D3D11 geometry-shader output limit.
    EmitVertex(stream, left, float3(0.0, 1.0, 0.0), vertexColor, 0.0);
    EmitVertex(stream, right, float3(0.0, 1.0, 0.0), vertexColor, 0.0);

    const int SEGMENTS = 2;
    [unroll]
    for (int s = 1; s <= SEGMENTS; s++)
    {
        float t = float(s) / float(SEGMENTS);
        float wind = sin(_Time.y * _WindSpeed + windPhase + t * 1.7) *
            _WindStrength * windScale * (0.25 + 0.75 * t) * height;
        // Push bends the blade away from colliders and flattens it.
        float h = height * (1.0 - 0.4 * saturate(push));
        float3 center = base + float3(0.0, t * h, 0.0) +
            float3(dirXZ.x, 0.0, dirXZ.y) * (lean * t * height) +
            windVec * wind + pushVec * (height * t);
        float w = width * (1.0 - 0.5 * t);
        float3 n = normalize(cross(center - left, widthDir));

        left = center - widthDir * (w * 0.5);
        right = center + widthDir * (w * 0.5);
        EmitVertex(stream, left, n, vertexColor, t);
        EmitVertex(stream, right, n, vertexColor, t);
    }
    stream.RestartStrip();
}

// 8 scalars → 1024 / 8 = 128; 3 ground verts + 20 blades × 6 verts = 123.
[maxvertexcount(123)]
void geom(triangle GSIn input[3],
    uint primitiveID : SV_PrimitiveID,
    inout TriangleStream<GSOut> stream)
{
    float3 p0 = input[0].positionWS;
    float3 p1 = input[1].positionWS;
    float3 p2 = input[2].positionWS;

    // Ground surface (the pass is Cull Off, so both faces draw).
    float3 faceNormal = normalize(cross(p1 - p0, p2 - p0));
    EmitVertex(stream, p0, faceNormal, input[0].vertexColor, -1.0);
    EmitVertex(stream, p1, faceNormal, input[1].vertexColor, -1.0);
    EmitVertex(stream, p2, faceNormal, input[2].vertexColor, -1.0);
    stream.RestartStrip();

    // Blade count scales with world area, the grass mask, and density.
    float area = length(cross(p1 - p0, p2 - p0)) * 0.5;
    float maskAvg = saturate(
        (input[0].vertexColor.r + input[1].vertexColor.r +
         input[2].vertexColor.r) * (1.0 / 3.0));
    int maxBlades = int(min(max(1.0, round(_MaxBladesPerTriangle)), 20.0));
    int count = int(clamp(
        area * _Density * (0.05 + 0.95 * maskAvg),
        0.0,
        float(maxBlades)));

    for (int b = 0; b < count; b++)
    {
        uint seed = primitiveID * 2654435761u + uint(b) * 1103515245u;
        float u = Rand01(seed);
        float v = Rand01(seed + 1u) * (1.0 - u);
        // Sample vertex colors barycentrically at the blade position so
        // painted paths follow the surface exactly.
        float4 c = (1.0 - u - v) * input[0].vertexColor +
            u * input[1].vertexColor + v * input[2].vertexColor;
        float mask = saturate(c.r);
        float3 bladePos = p0 + u * (p1 - p0) + v * (p2 - p0);
        EmitBlade(stream, bladePos, c, mask, seed);
    }
}

#endif
