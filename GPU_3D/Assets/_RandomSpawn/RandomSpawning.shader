Shader "Unlit/RandomSpawning_URP"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ItemData
            {
                float4 position;
            };
            StructuredBuffer<ItemData> itemBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float4 _Color;

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                float3 pos = itemBuffer[instanceID].position.xyz;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS + float4(pos,1.0));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }

            ENDHLSL
        }
    }
}
