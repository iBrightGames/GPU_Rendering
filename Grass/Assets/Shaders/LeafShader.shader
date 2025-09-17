
Shader "Custom/LeafInstanced"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.8, 0.3, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            StructuredBuffer<float3> _Positions;

            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float3 worldPos = _Positions[instanceID];       // Sphere vertex pozisyonu
                float3 finalPos = v.vertex.xyz + worldPos;      // Yaprak mesh + offset
                o.pos = UnityObjectToClipPos(float4(finalPos, 1.0));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
