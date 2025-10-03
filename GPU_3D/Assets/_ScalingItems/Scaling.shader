Shader "Unlit/Scaling"
{
    Properties {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader {
        Pass {
            Tags { "LightMode"="ForwardBase" }

            HLSLINCLUDE
            StructuredBuffer<float4x4> renderBuffer;
            float4 _Color;
            #include "UnityCG.cginc"
            ENDHLSL

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float3 vertex : POSITION;
            };

            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v, uint id : SV_InstanceID) {
                v2f o;
                float4x4 m = renderBuffer[id];
                float4 world = mul(m, float4(v.vertex, 1.0));
                o.pos = mul(UNITY_MATRIX_VP, world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                return _Color;
            }
            ENDHLSL
        }
    }
}
