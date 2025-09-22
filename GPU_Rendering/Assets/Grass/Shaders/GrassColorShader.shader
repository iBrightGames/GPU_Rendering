

Shader "Custom/GrassColorShader"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0,1,0,1)
        _BottomColor("Bottom Color", Color) = (0,0.3,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"


            float4 _TopColor;
            float4 _BottomColor;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float height : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 pos = v.vertex;
                // Instance transform uygula
                pos = UnityObjectToClipPos(pos); // instancing ile uyumlu
                o.vertex = pos;
                o.height = v.vertex.y;
                return o;
            }



            fixed4 frag(v2f i) : SV_Target
            {
                float t = saturate(i.height);
                return lerp(_BottomColor, _TopColor, t);
            }
            ENDCG
        }
    }
}
