Shader "Unlit/SimpleScaling"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Size("Size", Float)=1.0
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
                float3 position;
                float size;
                float4 color;
            };
            StructuredBuffer<ItemData> itemBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
            };

            float _Size;
            float4 _Color;

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;
                ItemData item = itemBuffer[instanceID];
                
                OUT.color = item.color;
    
                float3 instancePos = itemBuffer[instanceID].position.xyz;
                
                float finalScale = _Size;
                
                float3 scaledPosOS = IN.positionOS.xyz * finalScale; 
                
                float4 finalPos = float4(scaledPosOS + instancePos, 1.0);
                
                OUT.positionHCS = TransformObjectToHClip(finalPos);
                
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color;
            }

            ENDHLSL
        }
    }
}
