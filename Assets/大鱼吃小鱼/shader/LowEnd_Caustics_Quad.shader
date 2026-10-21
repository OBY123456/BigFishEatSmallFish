Shader "Custom/LowEnd_Caustics_Quad_HDR"
{
    Properties
    {
        [NoScaleOffset] _CausticsTex ("Caustics Texture (Grayscale)", 2D) = "white" {}
        
        // 【核心修改】：加上 [HDR] 标签，解锁高动态范围颜色拾取器
        [HDR] _Color ("Caustics Tint (HDR)", Color) = (0.5, 0.8, 1, 1) 
        
        _Intensity ("Intensity", Range(0, 10)) = 2.0
        
        [Header(Wave Settings)]
        _Scale ("Wave Scale", Float) = 0.5
        _Speed ("Wave Speed", Float) = 1.0
        
        [Header(Mask Settings)]
        _EdgeFade ("Edge Fade Sharpness", Range(0.1, 5.0)) = 1.5
    }
    SubShader
    {
        // 透明队列，不写深度，不挡视线
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        // 关闭深度写入，开启叠加混合 (Additive)
        ZWrite Off
        Blend One One
        Cull Off // 双面渲染

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog 

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; 
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0; 
                float2 localUV : TEXCOORD1;  
                UNITY_FOG_COORDS(2)
            };

            sampler2D _CausticsTex;
            fixed4 _Color;
            float _Intensity;
            float _Scale;
            float _Speed;
            float _EdgeFade;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.localUV = v.uv;
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 世界坐标UV
                float2 uv = i.worldPos.xz * _Scale;

                // 双层流动干涉
                float time = _Time.y * _Speed;
                float2 uv1 = uv + float2(time * 0.05, time * 0.03);
                float2 uv2 = uv * 1.25 - float2(time * 0.04, time * 0.06);

                fixed c1 = tex2D(_CausticsTex, uv1).r;
                fixed c2 = tex2D(_CausticsTex, uv2).r;
                fixed caustics = min(c1, c2); 

                // 边缘渐隐遮罩
                float2 fadeUV = abs(i.localUV - 0.5) * 2.0;
                float edgeMask = 1.0 - max(fadeUV.x, fadeUV.y);
                edgeMask = saturate(edgeMask * _EdgeFade); 

                // 颜色组合：因为 _Color 现在是 HDR，其 RGB 值可以大于 1
                fixed4 col = _Color * caustics * _Intensity * edgeMask;

                // 应用雾效
                UNITY_APPLY_FOG(i.fogCoord, col);
                
                return col;
            }
            ENDCG
        }
    }
}