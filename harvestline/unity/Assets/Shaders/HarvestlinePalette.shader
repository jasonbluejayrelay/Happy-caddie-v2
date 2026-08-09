// Harvestline single scene shader (spec §9): unlit base + a two-band lambert ramp
// + rim light, driven entirely by vertex colour. One material for the whole scene;
// GPU-instanced so the 24x24 grid of structures is a single draw call per mesh.
//
// The per-instance state tint (bottleneck colour / craft pulse) is a genuine
// per-instance property, supplied via MaterialPropertyBlock.SetVectorArray and read
// through an instancing buffer.
Shader "Harvestline/Palette"
{
    Properties
    {
        _ShadeStrength ("Shade Strength", Range(0,1)) = 0.35
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3.0
        _RimStrength ("Rim Strength", Range(0,1)) = 0.25
        [HDR] _EmissionTint ("Emission Tint", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Material-level (shared) parameters.
            CBUFFER_START(UnityPerMaterial)
                float _ShadeStrength;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float4 _EmissionTint;
            CBUFFER_END

            // Per-instance parameter: overrides _EmissionTint per structure.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(p.positionWS);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 n = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(n, mainLight.direction));

                // Two-band ramp: hard step into a lit band, rather than smooth lambert.
                float band = ndotl > 0.5 ? 1.0 : 0.55;
                float shade = lerp(1.0, band, _ShadeStrength);

                float3 baseCol = IN.color.rgb * mainLight.color * shade;

                // Rim light.
                float3 v = normalize(IN.viewDirWS);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;
                baseCol += _RimColor.rgb * rim;

                // Emission: material default plus per-instance override.
                float4 instTint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                baseCol += _EmissionTint.rgb + instTint.rgb;
                return half4(baseCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
