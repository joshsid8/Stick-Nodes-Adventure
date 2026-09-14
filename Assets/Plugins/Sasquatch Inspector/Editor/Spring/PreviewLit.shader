// Editor-only preview shader for 3D-mesh previews (Spring Sandbox + the Mesh Preview Lab). It shades a mesh
// from EXPLICIT light uniforms (a key light, a fill light, ambient, and an optional rim), so a sphere/cube
// reads as a properly shaded 3D object under EVERY render pipeline — including a URP 2D Renderer, which does
// no 3D directional lighting and otherwise leaves meshes flat/sprite-like (or, with unlit shaders, black).
//
// IMPORTANT: every uniform is declared in Properties (HideInInspector) so Material.HasProperty() returns true
// for them — otherwise SetVector/SetColor callers can't detect the shader and the uniforms stay 0, making the
// mesh render BLACK (the bug this replaced).
//
// One pass per pipeline, each tagged with a LightMode only that pipeline executes (no double-draw): ForwardBase
// (Built-in), UniversalForward (URP 3D), and CRUCIALLY Universal2D — the pass the URP 2D Renderer runs for a
// MeshRenderer. Lighting is computed from uniforms, so it never depends on URP/HDRP includes (compiles even
// when those packages are absent). Lives under Editor/ so it never ships in player builds.
Shader "Hidden/Sasquatch/PreviewLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.27, 0.55, 0.95, 1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        [HideInInspector] _PreviewKeyDir   ("Key Dir",   Vector) = (0.32, 0.55, -0.77, 0)
        [HideInInspector] _PreviewKeyColor ("Key Color", Color)  = (1, 1, 1, 1)
        [HideInInspector] _PreviewFillDir  ("Fill Dir",  Vector) = (-0.5, 0.1, 0.6, 0)
        [HideInInspector] _PreviewFillColor("Fill Color",Color)  = (0.22, 0.24, 0.32, 1)
        [HideInInspector] _PreviewAmbient  ("Ambient",   Color)  = (0.13, 0.13, 0.16, 1)
        [HideInInspector] _PreviewRimColor ("Rim Color", Color)  = (0, 0, 0, 0)
        [HideInInspector] _PreviewRimPower ("Rim Power", Float)  = 3
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    fixed4 _BaseColor;
    float4 _PreviewKeyDir;    fixed4 _PreviewKeyColor;
    float4 _PreviewFillDir;   fixed4 _PreviewFillColor;
    fixed4 _PreviewAmbient;
    fixed4 _PreviewRimColor;  float  _PreviewRimPower;

    struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
    struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.wn  = UnityObjectToWorldNormal(v.normal);
        o.wp  = mul(unity_ObjectToWorld, v.vertex).xyz;
        return o;
    }

    fixed4 frag (v2f i) : SV_Target
    {
        float3 n = normalize(i.wn);
        float ndl = saturate(dot(n, normalize(_PreviewKeyDir.xyz)));   // key (hard)
        float ndf = saturate(dot(n, normalize(_PreviewFillDir.xyz)));  // fill (soft)
        float3 viewDir = normalize(_WorldSpaceCameraPos - i.wp);
        float rim = pow(1.0 - saturate(dot(n, viewDir)), max(0.001, _PreviewRimPower));

        fixed3 diffuse = _PreviewAmbient.rgb + ndl * _PreviewKeyColor.rgb + ndf * _PreviewFillColor.rgb;
        fixed3 col = _BaseColor.rgb * diffuse + rim * _PreviewRimColor.rgb; // rim is an additive highlight
        return fixed4(col, _BaseColor.a);
    }
    ENDCG

    // -------- URP (Universal Render Pipeline): 2D Renderer AND Forward/Deferred 3D --------
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass { Tags { "LightMode"="UniversalForward" } CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
        ENDCG }

        // The pass the 2D Renderer executes for a MeshRenderer — required so the mesh draws (and shades) here.
        Pass { Tags { "LightMode"="Universal2D" } CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
        ENDCG }
    }

    // -------- HDRP --------
    // ORDER MATTERS: this SubShader must sit BEFORE the untagged Built-in one. An untagged SubShader matches
    // EVERY pipeline, so if it comes first HDRP selects it — and HDRP renders its ForwardBase-tagged pass through
    // RenderForwardError (ForwardBase is in HDRP's error-pass list) as solid MAGENTA (the hdrp matrix probe
    // measured 2045 magenta px before this reorder). With this SubShader first, HDRP picks it instead: a pass
    // with no LightMode tag is treated as LightMode="SRPDefaultUnlit", and HDRP's forward loop DOES draw
    // SRPDefaultUnlit passes — its own source declares them "a fall back to legacy unlit rendering" and lists
    // s_SRPDefaultUnlitName in m_ForwardAndForwardOnlyPassNames / m_AllForwardOpaquePassNames (HDRenderPipeline.cs).
    // So the self-lit preview renders correctly under HDRP with NO dependence on physical light units
    // (HdrpPrefersPreviewLit in RenderPipelineUtility prefers it there). Tagged RenderPipeline="HDRenderPipeline"
    // so URP (earlier SubShader) and Built-in (skips RenderPipeline-tagged SubShaders) never select it, keeping
    // their output byte-identical. The RenderPipeline tag is a plain string, so this compiles whether or not the
    // HDRP package is installed.
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
        ENDCG }
    }

    // -------- Built-in Render Pipeline (untagged ⇒ matches anything, so it must stay LAST) --------
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass { Tags { "LightMode"="ForwardBase" } CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
        ENDCG }
    }

    Fallback Off
}
