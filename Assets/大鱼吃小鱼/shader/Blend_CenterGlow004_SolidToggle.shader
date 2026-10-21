Shader "Hovl/Particles/Blend_CenterGlow004_SolidToggle"
{
    Properties
    {
        // --- 混合与实体开关 ---
        [Header(Blend and Solid Settings)]
        [Enum(Original_Solid, 1, Alpha_Blend, 5, Additive, 1, Multiply, 2, Screen, 1)] _SrcBlend("Blend Mode (Source)", Float) = 5
        [Enum(Original_Solid, 0, OneMinusSrcAlpha, 10, One, 1, Zero, 0, OneMinusSrcColor, 6)] _DstBlend("Blend Mode (Dest)", Float) = 10
        
        // 新增：深度写入开关（实体模式必须开启）
        [Enum(Off, 0, On, 1)] _ZWriteMode("ZWrite (Turn ON for Solid Model)", Float) = 0
        
        // 新增：透明度裁剪开关（防止实体模式下出现黑色矩形底板）
        [Toggle(_USE_ALPHACLIP)] _UseAlphaClip("Enable Alpha Cutoff (For Solid Edge)", Float) = 0
        _Cutoff("Alpha Cutoff Limit", Range(0, 1)) = 0.5
        
        [Header(Textures)]
        _MainTex("Main Texture (Alpha for Mask)", 2D) = "white" {}
        [Toggle(_USE_MAINTEX_MASK)] _UseMainTexMask("Enable MainTex Mask", Float) = 0
        _MainTexMask("MainTex Mask (R)", 2D) = "white" {}
        _Noise("Noise Texture", 2D) = "white" {}
        _NoiseMask("Noise Mask (Static)", 2D) = "white" {}

        [Header(Movement)]
        _SpeedMainTexUVNoiseZW("Main Speed (XY)", Vector) = (0,0,0,0)
        _NoiseSpeedUV("Noise Speed (XY)", Vector) = (0,0,0,0)

        [Header(Cyberpunk Glitch)]
        [Toggle(_USE_GLITCH)] _UseGlitch("Enable Glitch Effect", Float) = 0
        _GlitchIntensity("Jitter Intensity", Range(0, 0.5)) = 0.1
        _GlitchFrequency("Active Ratio (0=Never, 1=Always)", Range(0, 1)) = 0.2
        _GlitchInterval("Cycle Speed", Float) = 1.0
        _GlitchBlockSize("Mosaic Block Size", Float) = 20.0
        _GlitchRGBSplit("RGB Split Width", Range(0, 0.1)) = 0.05

        [Header(Distortion)]
        [Toggle(_USE_DISTORTION)] _UseDistortion("Enable Distortion", Float) = 0
        _DistortionTex("Distortion (RG)", 2D) = "bump" {}
        _DistortionStrength("Strength", Range(0, 0.2)) = 0.05
        _DistortionSpeed("Speed (XY)", Vector) = (0.1, 0.1, 0, 0)
        [Toggle(_USE_DISTORTION_MASK)] _UseDistortionMask("Use Mask Texture?", Float) = 0
        _DistortionMask("Distortion Mask (R)", 2D) = "white" {}

        [Header(Color and Glow)]
        _Color("Main Color", Color) = (0.5,0.5,0.5,1)
        _Emission("Brightness", Float) = 2
        _EmissionMap("Emission Map", 2D) = "black" {} 
        [HDR]_EmissionColor("Emission Color", Color) = (1,1,1,1)

        [Header(Outline)]
        [Toggle(_USE_OUTLINE)] _UseOutline("Enable Outline", Float) = 0
        [HDR]_OutlineColor("Outline Color", Color) = (0,0,0,0)
        _OutlineWidth("Width", Range(0, 0.03)) = 0.01
        _OutlinePulseSpeed("Pulse Speed", Float) = 5

        [Header(System)]
        _Opacity("Global Opacity", Float) = 1
        [Enum(Both,0,Back,1,Front,2)]_Cull("Culling", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        
        // 动态读取属性中的混合模式和深度写入设置
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWriteMode]
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _USE_OUTLINE
            #pragma shader_feature _USE_DISTORTION
            #pragma shader_feature _USE_DISTORTION_MASK
            #pragma shader_feature _USE_GLITCH
            #pragma shader_feature _USE_MAINTEX_MASK
            #pragma shader_feature _USE_ALPHACLIP
            
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Noise, _NoiseMask, _EmissionMap;
            float4 _MainTex_ST, _Noise_ST, _NoiseMask_ST, _EmissionMap_ST;
            
            float _DstBlend; 
            half _Cutoff;

            #ifdef _USE_MAINTEX_MASK
                sampler2D _MainTexMask;
                float4 _MainTexMask_ST;
            #endif

            half4 _SpeedMainTexUVNoiseZW, _NoiseSpeedUV, _Color, _EmissionColor;
            half _Emission, _Opacity;

            #ifdef _USE_DISTORTION
                // ... 保持原有变量声明 ...
                sampler2D _DistortionTex;
                float4 _DistortionTex_ST;
                half2 _DistortionSpeed;
                half _DistortionStrength;
                #ifdef _USE_DISTORTION_MASK
                    sampler2D _DistortionMask;
                    float4 _DistortionMask_ST;
                #endif
            #endif

            #ifdef _USE_OUTLINE
                half4 _OutlineColor;
                half _OutlineWidth, _OutlinePulseSpeed;
            #endif

            #ifdef _USE_GLITCH
                half _GlitchIntensity, _GlitchFrequency, _GlitchInterval, _GlitchBlockSize, _GlitchRGBSplit;
            #endif

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float time : TEXCOORD1; 
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.time = _Time.y; 
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                float2 baseUV = i.uv;
                half time = i.time;

                // 1. 扭曲逻辑
                #ifdef _USE_DISTORTION
                    float2 uvDist = baseUV * _DistortionTex_ST.xy + _DistortionTex_ST.zw + time * _DistortionSpeed;
                    half2 dist = (tex2D(_DistortionTex, uvDist).rg * 2 - 1) * _DistortionStrength;
                    
                    #ifdef _USE_DISTORTION_MASK
                        half dMask = tex2D(_DistortionMask, baseUV * _DistortionMask_ST.xy + _DistortionMask_ST.zw).r;
                        dist *= dMask;
                    #endif
                    
                    baseUV += dist;
                #endif

                // 2. 故障逻辑
                float2 uvMain, uvNoise;
                fixed4 mainTex;

                #ifdef _USE_GLITCH
                    half timePhase = frac(time * _GlitchInterval);
                    half glitchState = step(1.0 - _GlitchFrequency, timePhase);
                    
                    if (glitchState > 0.5) 
                    {
                        float2 blockUV = floor(baseUV * _GlitchBlockSize) / _GlitchBlockSize;
                        half randomHash = frac(sin(dot(blockUV + floor(time * 20.0), float2(12.9898, 78.233))) * 43758.5453); 
                        
                        half jitter = (randomHash * 2.0 - 1.0) * _GlitchIntensity;
                        baseUV.x += jitter;
                        
                        uvMain = baseUV * _MainTex_ST.xy + _MainTex_ST.zw + time * _SpeedMainTexUVNoiseZW.xy;
                        uvNoise = baseUV * _Noise_ST.xy + _Noise_ST.zw + time * _NoiseSpeedUV.xy;
                        
                        half splitDist = _GlitchRGBSplit * randomHash;
                        mainTex.r = tex2D(_MainTex, uvMain + float2(splitDist, randomHash * splitDist * 0.5)).r;
                        mainTex.g = tex2D(_MainTex, uvMain).g;
                        mainTex.b = tex2D(_MainTex, uvMain - float2(splitDist, randomHash * splitDist * 0.5)).b;
                        mainTex.a = tex2D(_MainTex, uvMain).a;
                    } 
                    else 
                    {
                        uvMain = baseUV * _MainTex_ST.xy + _MainTex_ST.zw + time * _SpeedMainTexUVNoiseZW.xy;
                        uvNoise = baseUV * _Noise_ST.xy + _Noise_ST.zw + time * _NoiseSpeedUV.xy;
                        mainTex = tex2D(_MainTex, uvMain);
                    }
                #else
                    uvMain = baseUV * _MainTex_ST.xy + _MainTex_ST.zw + time * _SpeedMainTexUVNoiseZW.xy;
                    uvNoise = baseUV * _Noise_ST.xy + _Noise_ST.zw + time * _NoiseSpeedUV.xy;
                    mainTex = tex2D(_MainTex, uvMain);
                #endif

                // 3. 核心合成
                half3 noise = tex2D(_Noise, uvNoise).rgb * tex2D(_NoiseMask, baseUV * _NoiseMask_ST.xy + _NoiseMask_ST.zw).rgb;
                half3 finalRGB = mainTex.rgb * _Color.rgb * i.color.rgb * _Emission;

                // 4. 发光
                half3 emi = tex2D(_EmissionMap, baseUV * _EmissionMap_ST.xy + _EmissionMap_ST.zw).rgb;
                finalRGB += emi * noise * _EmissionColor.rgb * _Emission;

                half finalAlpha = mainTex.a * _Opacity * i.color.a;

                // 5. 描边逻辑
                #ifdef _USE_OUTLINE
                    half pulse = (1.0 + 0.3 * sin(time * _OutlinePulseSpeed));
                    half width = _OutlineWidth * pulse;
                    
                    half aR = tex2D(_MainTex, uvMain + float2(width, 0)).a;
                    half aL = tex2D(_MainTex, uvMain + float2(-width, 0)).a;
                    half aU = tex2D(_MainTex, uvMain + float2(0, width)).a;
                    half aD = tex2D(_MainTex, uvMain + float2(0, -width)).a;
                    
                    half outlineMask = saturate((max(max(aR, aL), max(aU, aD)) - min(min(aR, aL), min(aU, aD))) * 2.0);
                    
                    finalRGB += _OutlineColor.rgb * outlineMask * i.color.a * noise;
                    finalAlpha = saturate(finalAlpha + outlineMask * _Opacity * i.color.a);
                #endif

                // 6. 遮罩逻辑
                #ifdef _USE_MAINTEX_MASK
                    half mainMask = tex2D(_MainTexMask, baseUV * _MainTexMask_ST.xy + _MainTexMask_ST.zw).r;
                    finalAlpha *= mainMask;
                    
                    if (_DstBlend < 9.0) 
                    {
                        finalRGB *= mainMask; 
                    }
                #endif

                if (_DstBlend < 9.0) 
                {
                    finalRGB *= (_Opacity * i.color.a);
                }

                // 7. 新增：透明度裁剪（实体模式下剔除透明底边）
                #ifdef _USE_ALPHACLIP
                    clip(finalAlpha - _Cutoff);
                #endif

                return fixed4(finalRGB, finalAlpha);
            }
            ENDCG
        }
    }
}