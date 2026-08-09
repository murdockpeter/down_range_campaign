Shader "DownRange/OneStarTriplanarPaint"
{
    Properties
    {
        _MainTex ("Faction paint", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CamoScale ("Pattern scale", Float) = 1.7
        _EquipmentColor ("Equipment", Color) = (0.45,0.32,0.18,1)
        _WeaponColor ("Weapons and boots", Color) = (0.07,0.08,0.075,1)
        _SkinColor ("Faces and hands", Color) = (0.72,0.48,0.36,1)
        _ModelHeight ("Model height", Float) = 2.0
        _ModelRadius ("Model radius", Float) = 0.8
        _BaseCutoff ("Integral base height", Float) = 0.17
        _PaintMode ("Paint mode", Float) = 1.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _EquipmentColor;
        fixed4 _WeaponColor;
        fixed4 _SkinColor;
        half _CamoScale;
        half _ModelHeight;
        half _ModelRadius;
        half _BaseCutoff;
        half _PaintMode;
        half _Smoothness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            float3 objectPosition = mul(unity_WorldToObject, float4(input.worldPos, 1.0)).xyz;
            float3 objectNormal = normalize(mul((float3x3)unity_WorldToObject, input.worldNormal));
            float3 weights = pow(abs(objectNormal), 4.0);
            weights /= max(weights.x + weights.y + weights.z, 0.0001);

            fixed3 projectedX = tex2D(_MainTex, objectPosition.zy * _CamoScale).rgb;
            fixed3 projectedY = tex2D(_MainTex, objectPosition.xz * _CamoScale).rgb;
            fixed3 projectedZ = tex2D(_MainTex, objectPosition.xy * _CamoScale).rgb;
            fixed3 paint = (projectedX * weights.x + projectedY * weights.y + projectedZ * weights.z) * _Color.rgb;
            float height = saturate(objectPosition.y / max(_ModelHeight, 0.01));
            float radius = length(objectPosition.xz) / max(_ModelRadius, 0.01);

            if (_PaintMode < 1.5)
            {
                // Printable figures often include a tall integral plinth.  Keep it
                // uniformly dark before transitioning into the boots and legs.
                float baseMask = 1.0 - smoothstep(_BaseCutoff - 0.025, _BaseCutoff + 0.025, height);
                float bootMask = smoothstep(0.17, 0.22, height) * (1.0 - smoothstep(0.28, 0.34, height));
                float vestMask = smoothstep(0.34, 0.42, height) * (1.0 - smoothstep(0.68, 0.77, height));
                vestMask *= lerp(0.72, 1.0, 1.0 - saturate(abs(objectNormal.y)));
                float protrudingGear = smoothstep(0.58, 0.88, radius) * smoothstep(0.25, 0.36, height) * (1.0 - smoothstep(0.76, 0.86, height));
                float weaponMask = protrudingGear * (1.0 - vestMask * 0.82);
                float normalizedZ = objectPosition.z / max(_ModelRadius, 0.01);
                // Exposed face sits below the helmet rim on this sculpt.  Use a
                // short lower band and a forward-normal test to avoid painting
                // a stripe around the helmet shell.
                float faceHeight = smoothstep(0.665, 0.70, height) * (1.0 - smoothstep(0.79, 0.82, height));
                float faceFront = smoothstep(-0.10, 0.02, normalizedZ) * smoothstep(0.12, 0.50, objectNormal.z);
                float faceMask = faceHeight * faceFront;
                float antennaHeight = smoothstep(0.84, 0.91, height);
                float antennaRear = 1.0 - smoothstep(-0.48, -0.34, normalizedZ);
                float antennaMask = antennaHeight * antennaRear;
                paint = lerp(paint, _EquipmentColor.rgb, saturate(vestMask * 0.88));
                paint = lerp(paint, _WeaponColor.rgb, saturate(max(max(baseMask, bootMask * 0.82), weaponMask * 0.9)));
                paint = lerp(paint, _WeaponColor.rgb, saturate(antennaMask * 0.96));
                paint = lerp(paint, _SkinColor.rgb, saturate(faceMask * 0.94));
            }
            else if (_PaintMode < 2.5)
            {
                float runningGear = 1.0 - smoothstep(0.18, 0.31, height);
                float upperEquipment = smoothstep(0.62, 0.78, height) * (0.45 + 0.35 * saturate(objectNormal.y));
                paint = lerp(paint, _EquipmentColor.rgb, saturate(upperEquipment));
                paint = lerp(paint, _WeaponColor.rgb, saturate(runningGear * 0.94));
            }
            else
            {
                float uasHardware = smoothstep(0.42, 0.72, radius);
                paint = lerp(paint, _WeaponColor.rgb, saturate(uasHardware * 0.82));
            }

            output.Albedo = paint;
            output.Metallic = 0.0;
            output.Smoothness = _Smoothness;
            output.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
