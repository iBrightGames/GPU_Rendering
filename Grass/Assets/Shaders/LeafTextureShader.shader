Shader "Custom/LeafInstancedTex"
{
    Properties
    {
        _MainTex("Leaf Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float3 worldPos = _Positions[instanceID];
                float3 finalPos = v.vertex.xyz + worldPos;

                o.pos = UnityObjectToClipPos(float4(finalPos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 texCol = tex2D(_MainTex, i.uv);
                return texCol * _Color;
            }
            ENDCG
        }
    }
}
