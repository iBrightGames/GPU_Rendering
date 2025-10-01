Shader "Custom/InstanceRenderer"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma multi_compile_instancing
        #pragma instancing_options procedural:setup
        #pragma target 4.5

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };

        struct InstanceData
        {
            float3 position;
            float3 velocity;
            float3 rotation;
            int active;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<InstanceData> _InstanceData;
        #endif

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                InstanceData data = _InstanceData[unity_InstanceID];
                
                // Apply position
                unity_ObjectToWorld._m03_m13_m23_m33 = float4(data.position, 1);
                unity_ObjectToWorld._m00_m11_m22 = 1;
                unity_ObjectToWorld._m01_m02_m10_m12_m20_m21 = 0;
                
                // Apply rotation (simple Euler angles)
                float3 rot = data.rotation;
                float cx = cos(rot.x);
                float sx = sin(rot.x);
                float cy = cos(rot.y);
                float sy = sin(rot.y);
                float cz = cos(rot.z);
                float sz = sin(rot.z);
                
                // Rotation matrix
                unity_ObjectToWorld._m00_m10_m20 = float3(cy * cz, cy * sz, -sy);
                unity_ObjectToWorld._m01_m11_m21 = float3(sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy);
                unity_ObjectToWorld._m02_m12_m22 = float3(cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy);
                
                unity_WorldToObject = unity_ObjectToWorld;
                unity_WorldToObject._m03_m13_m23 = -data.position;
            #endif
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
