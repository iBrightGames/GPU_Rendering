Shader "CustomUnlit/SingleSpriteCompute_GPUActive"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Scale("Scale", Float) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // -------------------------------
            // Compute Buffer Structure
            // -------------------------------
            struct InstanceData
            {
                float3 position;
                int active;
            };

            StructuredBuffer<InstanceData> _InstanceDataBuffer;
            float _Scale;

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            // -------------------------------
            // Vertex shader
            // -------------------------------
            v2f vert(appdata v)
            {
                v2f o;

                // InstanceID ile buffer'dan veriyi al
                InstanceData data = _InstanceDataBuffer[v.instanceID];

                // Aktiflik kontrolü
                if (data.active == 0)
                {
                    // Pasif instance'ları tamamen yok et
                    o.pos = float4(0, 0, 0, 0);
                    o.uv = float2(0, 0);
                    return o;
                }

                // Aktif instance'ları çiz
                float4 worldPos = float4(data.position, 1.0) + float4(v.vertex * _Scale, 0);
                o.pos = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // -------------------------------
            // Fragment shader
            // -------------------------------
            fixed4 frag(v2f i) : SV_Target
            {
                // Geçersiz pozisyonları atla
                if (i.pos.w == 0) discard;
                return tex2D(_MainTex, i.uv);
            }

            ENDHLSL
        }
    }
}