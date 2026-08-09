Shader "DownRange/TerrainGrid"
{
    Properties { _Smoothness ("Smoothness", Range(0,1)) = 0.05 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 180
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        half _Smoothness;
        struct Input { fixed4 color : COLOR; };
        void vert(inout appdata_full vertex, out Input output) { UNITY_INITIALIZE_OUTPUT(Input, output); output.color = vertex.color; }
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            output.Albedo = input.color.rgb; output.Metallic = 0; output.Smoothness = _Smoothness; output.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
