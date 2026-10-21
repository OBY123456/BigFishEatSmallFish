Shader "Hovl/Particles/Blend_CenterGlow003"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        _NoiseMask("Noise Mask", 2D) = "white" {}
        _Flow("Flow", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}

        _SpeedMainTexUVNoiseZW("Speed MainTex U/V", Vector) = (0,0,0,0)
        _NoiseSpeedUV("Noise UV Speed", Vector) = (0,0,0,0)

        _DistortionSpeedXYPowerZ("Distortion Speed XY Power Z", Vector) = (0,0,0,0)
        _Emission("Emission Multiplier", Float) = 2
        _Color("Color", Color) = (0.5,0.5,0.5,1)
        
        // --- 新增发光贴图属性 ---
        _EmissionMap("Emission Map", 2D) = "black" {} 
        [HDR]_EmissionColor("Emission Color", Color) = (1,1,1,1) // 使用 HDR 颜色以便更好地控制发光强度

        // --- 动态描边属性 ---
        [HDR]_OutlineColor("Outline Color", Color) = (0,0,0,0) // HDR描边颜色，默认为空/黑不显示
        _OutlineWidth("Outline Width", Range(0, 0.1)) = 0.02
        _OutlinePulseSpeed("Outline Pulse Speed", Float) = 5 // 动态呼吸频率

        _Opacity("Opacity", Float) = 1

        [Enum(Both,0,Back,1,Front,2)]_Cull("Render Face", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        
        // 使用标准的透明度混合
        Blend SrcAlpha OneMinusSrcAlpha 
        
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Noise;
            sampler2D _NoiseMask;
            
            // --- 新增发光贴图采样器 ---
            sampler2D _EmissionMap;

            float4 _MainTex_ST;
            float4 _Noise_ST;
            float4 _NoiseMask_ST;
            
            // --- 新增发光贴图的 ST (Tiling/Offset) ---
            float4 _EmissionMap_ST; 

            float4 _SpeedMainTexUVNoiseZW;
            float4 _NoiseSpeedUV;

            float4 _Color;
            float _Emission;
            float _Opacity;
            
            // --- 新增发光颜色变量 ---
            float4 _EmissionColor;

            // --- 新增描边变量 ---
            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlinePulseSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR; // 粒子系统的颜色数据
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color; 
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Main UV
                float2 uvMain = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                uvMain += _Time.y * _SpeedMainTexUVNoiseZW.xy;

                // Noise UV
                float2 uvNoise = i.uv * _Noise_ST.xy + _Noise_ST.zw;
                uvNoise += _Time.y * _NoiseSpeedUV.xy;

                // Noise Mask UV
                float2 uvNoiseMask = i.uv * _NoiseMask_ST.xy + _NoiseMask_ST.zw;
                
                // Emission UV (新增发光贴图 UV 计算)
                float2 uvEmission = i.uv * _EmissionMap_ST.xy + _EmissionMap_ST.zw;

                fixed4 mainTex = tex2D(_MainTex, uvMain);
                fixed4 noiseTex = tex2D(_Noise, uvNoise);
                fixed4 noiseMask = tex2D(_NoiseMask, uvNoiseMask);
                
                // --- 采样发光贴图 ---
                fixed4 emiTex = tex2D(_EmissionMap, uvEmission);

                // Noise 与 Noise Mask 相交 (乘法混合，提取交集)
                float3 noiseBlend = noiseTex.rgb * noiseMask.rgb;

                // 计算基础颜色 (Noise 不再影响主贴图)
                float3 col = mainTex.rgb * _Color.rgb * i.color.rgb;
                
                // 保留原有的整体发光增强倍数
                col *= _Emission;

                // --- 计算附加发光 ---
                // 发光贴图 RGB * Noise * 自定义发光颜色 * 整体 Emission 控制
                float3 finalEmission = emiTex.rgb * noiseBlend * _EmissionColor.rgb * _Emission;
                
                // 将发光叠加到基础颜色上 (Additive 混合)
                col += finalEmission;

                // --- 动态边缘描边 (优化版：内外双向描边) ---
                // 计算随时间脉冲的动态描边宽度
                float currentOutlineWidth = _OutlineWidth * (1.0 + 0.4 * sin(_Time.y * _OutlinePulseSpeed));
                
                // 采样周围4个方向的Alpha，用于检测边缘
                float alphaRight = tex2D(_MainTex, uvMain + float2(currentOutlineWidth, 0)).a;
                float alphaLeft  = tex2D(_MainTex, uvMain + float2(-currentOutlineWidth, 0)).a;
                float alphaUp    = tex2D(_MainTex, uvMain + float2(0, currentOutlineWidth)).a;
                float alphaDown  = tex2D(_MainTex, uvMain + float2(0, -currentOutlineWidth)).a;
                
                // 获取周围最大和最小的透明度
                float maxSurroundAlpha = max(max(alphaRight, alphaLeft), max(alphaUp, alphaDown));
                float minSurroundAlpha = min(min(alphaRight, alphaLeft), min(alphaUp, alphaDown));
                
                // 边缘蒙版：最大值减去最小值 (即包含向外扩散和向内收缩的边缘)
                float outlineMask = saturate((maxSurroundAlpha - minSurroundAlpha) * 2.0);
                
                // 描边颜色：直接使用 HDR RGB，不再受制于 _OutlineColor.a，方便面板调节
                float3 finalOutlineCol = _OutlineColor.rgb * outlineMask * i.color.a * noiseBlend;
                
                // 将描边发光叠加到最终颜色中
                col += finalOutlineCol;

                // 透明度计算 (把描边区域的透明度也加进去，让边缘显现实体)
                float alpha = mainTex.a * _Opacity * i.color.a;
                alpha = saturate(alpha + outlineMask * _Opacity * i.color.a);

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}