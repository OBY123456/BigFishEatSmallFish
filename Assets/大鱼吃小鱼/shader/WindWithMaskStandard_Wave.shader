Shader "Custom/WindWithMaskStandard_Wave"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _Color("Color Tint", Color) = (1,1,1,1)

        _AmbientTex("Ambient Light Map", 2D) = "white" {}

        _WindMask("Wind Mask (black=no move)", 2D) = "white" {}
        _WindStrength("Wind Strength", Range(0,2)) = 0.5

        // ====== 波浪控制参数 ======
        _WaveAmplitude("Wave Amplitude", Range(0,1)) = 0.1
        _WaveFrequency("Wave Frequency", Range(0,10)) = 3
        _WaveSpeed("Wave Speed", Range(0,5)) = 1
        _WaveAxis("Wave Axis (XYZ)", Vector) = (0,1,0,0)

        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags{ "RenderType"="TransparentCutout" "Queue"="AlphaTest" }

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows alphatest:_Cutoff vertex:vert addshadow

        sampler2D _MainTex;
        sampler2D _AmbientTex;
        sampler2D _WindMask;

        fixed4 _Color;

        float _WindStrength;

        // 新增波浪控制参数
        float _WaveAmplitude;
        float _WaveFrequency;
        float _WaveSpeed;
        float3 _WaveAxis;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_AmbientTex;
            float2 uv_WindMask;
        };

        //==========================
        // 顶点动画：波浪运动
        //==========================
        void vert(inout appdata_full v)
        {
            // 取遮罩
            float mask = tex2Dlod(_WindMask, float4(v.texcoord.xy, 0, 0)).r;

            if(mask <= 0.001) return;

            float t = _Time.y * _WaveSpeed;

            // 顶点位置（世界空间 xz 用来做波）
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

            // 波浪公式：沿 xz 传播
            float wave = sin(worldPos.x * _WaveFrequency + worldPos.z * _WaveFrequency + t);

            // 波浪强度
            wave *= (_WaveAmplitude * mask * _WindStrength);

            // 使用可调的轴方向添加位移（常用 Y 轴）
            float3 movement = normalize(_WaveAxis) * wave;

            v.vertex.xyz += movement;
        }

        //==========================
        // 表面着色
        //==========================
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            fixed4 ambient = tex2D(_AmbientTex, IN.uv_AmbientTex);

            o.Albedo = c.rgb * ambient.rgb;
            o.Alpha = c.a;
        }

        ENDCG
    }

    FallBack "Diffuse"
}
