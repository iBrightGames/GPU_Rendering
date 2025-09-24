Shader "Custom/GPUInstancingPos"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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

            struct appdata
            {
                float3 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                float3 worldPos = _Positions[id];
                float4 pos = float4(v.vertex + worldPos, 1.0);
                o.pos = UnityObjectToClipPos(pos);
                o.uv = float2(0,0);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(1,1,1,1);
            }
            ENDCG
        }
    }
}
