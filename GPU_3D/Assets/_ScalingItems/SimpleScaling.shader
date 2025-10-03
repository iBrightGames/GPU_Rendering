

Shader "Unlit/SimpleScaling"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct ItemData
            {
                float3 position;
                float size;
                float4 color;
            };

            StructuredBuffer<ItemData> itemBuffer;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;

                ItemData item = itemBuffer[instanceID];

                float3 scaled    = IN.positionOS * item.size;
                float3 objectPos = scaled + item.position;

                // Object → World
                float4 worldPos = mul(GetObjectToWorldMatrix(), float4(objectPos, 1.0));

                OUT.positionWS  = worldPos.xyz;
                OUT.positionHCS = TransformWorldToHClip(worldPos.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);

                // ShadowCoord varying
                OUT.shadowCoord = TransformWorldToShadowCoord(worldPos.xyz);

                OUT.color = (half4)item.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 albedo     = IN.color.rgb;
                half3 normalWS   = normalize(IN.normalWS);
                float3 positionWS = IN.positionWS;

                // Ana ışık + gölge
                Light mainLight = GetMainLight(IN.shadowCoord);

                // Ambient
                half3 ambientLight = SampleSH(normalWS);

                // Diffuse
                half3 diffuse = LightingLambert(mainLight.color, mainLight.direction, normalWS);
                diffuse *= mainLight.shadowAttenuation;

                half3 final = ambientLight + diffuse;

                return half4(albedo * final, IN.color.a);
            }

            ENDHLSL
        }
    }
}
