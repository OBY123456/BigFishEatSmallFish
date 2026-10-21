Shader "Hidden/UnderwaterEffectShader"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _NoiseTex ("Distortion Noise", 2D) = "grey" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthTexture; 
            sampler2D _NoiseTex;

            float4 _WaterColor;
            float _FogDensity;
            
            float _DistortIntensity;
            float _DistortArea;
            float4 _NoiseScale; // XY控制缩放
            float4 _NoiseSpeed; // XY控制流动位移

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // --- 1. 计算屏幕顶部遮罩 ---
                float screenY = i.uv.y;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    screenY = 1.0 - screenY;
                #endif
                
                // smoothstep: _DistortArea以下不扭曲(0)，以上平滑过渡到最大扭曲(1)
                float topMask = smoothstep(_DistortArea, 1.0, screenY);

                // --- 2. 贴图UV缩放与位移(流动)计算 ---
                // i.uv * 缩放比例 + 时间 * 流动速度
                float2 noiseUV = i.uv * _NoiseScale.xy + _Time.y * _NoiseSpeed.xy;
                
                // 采样噪声贴图计算扭曲向量
                float n1 = tex2D(_NoiseTex, noiseUV).r;
                float n2 = tex2D(_NoiseTex, noiseUV + float2(0.35, 0.71)).r; // 稍微偏移采样第二次，打破规律感
                float2 distortionOffset = float2(n1 - 0.5, n2 - 0.5) * 2.0; 

                // 结合顶部遮罩和强度计算最终UV
                float2 distortedUV = i.uv + distortionOffset * _DistortIntensity * topMask;
                distortedUV = saturate(distortedUV); // 限制在0~1，防止边缘采黑

                // --- 3. 渲染主画面与雾效 ---
                fixed4 col = tex2D(_MainTex, distortedUV);
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, distortedUV);
                float linearDepth = LinearEyeDepth(rawDepth);

                // 深度雾效 (越远越偏向海水颜色)
                float fogFactor = saturate(exp(-linearDepth * _FogDensity));
                col.rgb = lerp(_WaterColor.rgb, col.rgb, fogFactor);

                return col;
            }
            ENDCG
        }
    }
}