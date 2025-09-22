// Shader "Custom/GrassInstancing"
// {
//     Properties
//     {
//         _MainTex ("Texture", 2D) = "white" {}
//         _Width ("Width", Float) = 0.1
//         _Height ("Height", Float) = 1.0
//     }
    
//     SubShader
//     {
//         Tags { "RenderType"="Opaque" }
//         LOD 100
        
//         Pass
//         {
//             CGPROGRAM
//             #pragma vertex vert
//             #pragma fragment frag
//             #pragma multi_compile_instancing
//             #pragma instancing_options procedural:setup

//             #include "UnityCG.cginc"

//             struct appdata
//             {
//                 float4 vertex : POSITION;
//                 float2 uv : TEXCOORD0;
//                 uint instanceID : SV_InstanceID;
//             };

//             struct v2f
//             {
//                 float2 uv : TEXCOORD0;
//                 float4 vertex : SV_POSITION;
//                 float4 color : COLOR;
//             };

//             sampler2D _MainTex;
//             float4 _MainTex_ST;
//             float _Width;
//             float _Height;

//             // Compute buffers
//             StructuredBuffer<float4> _PosScaleBuffer;
//             StructuredBuffer<float4> _ColorBuffer;
//             int _GrassCount;

//             void setup()
//             {
//                 // This is required for procedural instancing but can be empty
//             }

//             v2f vert (appdata v, uint instanceID : SV_InstanceID)
//             {
//                 v2f o;
                
//                 if (instanceID >= _GrassCount)
//                 {
//                     o.vertex = float4(0, 0, 0, 1);
//                     o.uv = v.uv;
//                     o.color = float4(1, 0, 0, 1); // Red for error
//                     return o;
//                 }

//                 // Get instance data
//                 float4 posScale = _PosScaleBuffer[instanceID];
//                 float4 color = _ColorBuffer[instanceID];

//                 // Position and scale
//                 float3 worldPos = float3(posScale.x, posScale.y, posScale.z);
//                 float scale = posScale.w;

//                 // Create grass quad (simple version)
//                 float3 vertexOffset = float3(
//                     v.vertex.x * _Width * scale,
//                     v.vertex.y * _Height * scale,
//                     v.vertex.z * _Width * scale
//                 );

//                 float3 worldPosition = worldPos + vertexOffset;
//                 o.vertex = UnityObjectToClipPos(float4(worldPosition, 1.0));
//                 o.uv = TRANSFORM_TEX(v.uv, _MainTex);
//                 o.color = color;

//                 return o;
//             }

//             fixed4 frag (v2f i) : SV_Target
//             {
//                 if (i.color.r == 1 && i.color.g == 0 && i.color.b == 1) // Error color
//                     return fixed4(1, 0, 0, 1);
                    
//                 fixed4 col = tex2D(_MainTex, i.uv) * i.color;
//                 return col;
//             }
//             ENDCG
//         }
//     }
// }
Shader "Custom/GrassProcedural_URP"
{
    Properties
    {
        _Width ("Grass Width", Float) = 0.1
        _Height ("Grass Height", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            Lighting Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _PosScaleBuffer;
            StructuredBuffer<float4> _ColorBuffer;

            float _Width;
            float _Height;

            struct appdata
            {
                uint vertexID : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;

                uint bladeIndex = v.vertexID / 6;
                uint cornerIndex = v.vertexID % 6;

                float4 blade = _PosScaleBuffer[bladeIndex];
                float4 bladeColor = _ColorBuffer[bladeIndex];

                float halfW = _Width * 0.5 * blade.w;
                float h = _Height * blade.w;

                float3 localPos;
                if (cornerIndex == 0) localPos = float3(-halfW, 0, 0);
                if (cornerIndex == 1) localPos = float3(-halfW, h, 0);
                if (cornerIndex == 2) localPos = float3( halfW, h, 0);
                if (cornerIndex == 3) localPos = float3(-halfW, 0, 0);
                if (cornerIndex == 4) localPos = float3( halfW, h, 0);
                if (cornerIndex == 5) localPos = float3( halfW, 0, 0);

                float3 worldPos = float3(blade.x, blade.y, blade.z) + localPos;

                o.pos = TransformWorldToHClip(worldPos);
                o.color = bladeColor;

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }

            ENDHLSL
        }
    }
}
