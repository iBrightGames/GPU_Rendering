Shader "Custom/GPUInstancingTest"
{
    Properties
    {
        _Color("Color", Color) = (1,0,0,1) // kırmızı varsayılan
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
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
                float3 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v, uint id : SV_InstanceID)
            {
                v2f o;
                float3 worldPos = _Positions[id];
                o.pos = UnityObjectToClipPos(float4(v.vertex + worldPos, 1.0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
