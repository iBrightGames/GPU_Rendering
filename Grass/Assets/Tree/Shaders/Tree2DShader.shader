// // Shader "Custom/Leaf2D"
// // {
// //     Properties
// //     {
// //         _ColorLeaf("Leaf Color", Color) = (0,1,0,1)
// //         _ColorBranch("Branch Color", Color) = (0.5,0.3,0.1,1)
// //     }
// //     SubShader
// //     {
// //         Tags { "RenderType"="Opaque" }
// //         Pass
// //         {
// //             CGPROGRAM
// //             #pragma vertex vert
// //             #pragma fragment frag
// //             #include "UnityCG.cginc"

// //             struct LeafData
// //             {
// //                 float3 position;
// //                 float scale;
// //                 float isBranch;
// //             };

// //             StructuredBuffer<LeafData> _LeafBuffer;

// //             struct appdata
// //             {
// //                 float3 vertex : POSITION;
// //                 uint instanceID : SV_InstanceID;
// //             };

// //             struct v2f
// //             {
// //                 float4 pos : SV_POSITION;
// //                 float isBranch : TEXCOORD0;
// //             };

// //             v2f vert(appdata v)
// //             {
// //                 v2f o;
// //                 LeafData leaf = _LeafBuffer[v.instanceID];
// //                 float3 pos = v.vertex * leaf.scale + leaf.position;
// //                 o.pos = UnityObjectToClipPos(pos);
// //                 o.isBranch = leaf.isBranch;
// //                 return o;
// //             }

// //             fixed4 _ColorLeaf;
// //             fixed4 _ColorBranch;

// //             fixed4 frag(v2f i) : SV_Target
// //             {
// //                 return lerp(_ColorLeaf, _ColorBranch, i.isBranch);
// //             }
// //             ENDCG
// //         }
// //     }
// // }
// Shader "Custom/Tree2D"
// {
//     Properties
//     {
//         _ColorLeaf("Leaf Color", Color) = (0,1,0,1)
//         _ColorBranch("Branch Color", Color) = (0.5,0.3,0.1,1)
//     }
//     SubShader
//     {
//         Tags { "RenderType"="Opaque" }
//         Pass
//         {
//             CGPROGRAM
//             #pragma vertex vert
//             #pragma fragment frag
//             #include "UnityCG.cginc"

//             struct TreeData
//             {
//                 float3 position;
//                 float scale;
//                 float rotation;
//                 float isBranch;
//             };

//             StructuredBuffer<TreeData> _TreeBuffer;

//             struct appdata
//             {
//                 float3 vertex : POSITION;
//                 uint instanceID : SV_InstanceID;
//             };

//             struct v2f
//             {
//                 float4 pos : SV_POSITION;
//                 float isBranch : TEXCOORD0;
//             };

//             v2f vert(appdata v)
//             {
//                 v2f o;
//                 TreeData t = _TreeBuffer[v.instanceID];

//                 // Rotate vertex
//                 float c = cos(t.rotation);
//                 float s = sin(t.rotation);
//                 float3 rotated = float3(v.vertex.x * c - v.vertex.y * s, v.vertex.x * s + v.vertex.y * c, 0);

//                 // Scale and translate
//                 float3 pos = rotated * t.scale + t.position;
//                 o.pos = UnityObjectToClipPos(pos);
//                 o.isBranch = t.isBranch;
//                 return o;
//             }

//             fixed4 _ColorLeaf;
//             fixed4 _ColorBranch;

//             fixed4 frag(v2f i) : SV_Target
//             {
//                 return lerp(_ColorLeaf, _ColorBranch, i.isBranch);
//             }
//             ENDCG
//         }
//     }
// }
Shader "Custom/CircleGPU"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct CirclePoint
            {
                float3 position;
            };

            StructuredBuffer<CirclePoint> _CircleBuffer;

            struct appdata
            {
                uint vertexID : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                CirclePoint p = _CircleBuffer[v.vertexID];
                o.pos = UnityObjectToClipPos(p.position);
                return o;
            }

            fixed4 _Color;

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
