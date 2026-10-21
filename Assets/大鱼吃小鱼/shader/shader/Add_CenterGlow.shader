Shader "Hovl/Particles/Add_CenterGlow"
{
	Properties
	{	
		_MainTex("MainTex", 2D) = "white" {}
		_Noise("Noise", 2D) = "white" {}
		_Flow("Flow", 2D) = "white" {}
		_Mask("Mask", 2D) = "white" {}

		_SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)
		_DistortionSpeedXYPowerZ("Distortion Speed XY Power Z", Vector) = (0,0,0,0)

		// ===== 新增：Noise UV 流动控制 =====
		[Toggle] _UseNoiseFlow ("Use Noise UV Flow", Float) = 1
		_NoiseFlowSpeed ("Noise Flow Speed (XY)", Vector) = (0,0,0,0)

		_Emission("Emission", Float) = 2
		_Color("Color", Color) = (0.5,0.5,0.5,1)

		[Toggle]_Usecenterglow("Use center glow?", Float) = 0

		[MaterialToggle] _Usedepth ("Use depth?", Float ) = 0
        _Depthpower ("Depth power", Float ) = 1

		[Enum(Cull Off,0, Cull Front,1, Cull Back,2)] _CullMode("Culling", Float) = 0
		[Enum(One,1,OneMinuSrcAlpha,6)] _Blend2 ("Blend mode subset", Float) = 1

		[HideInInspector] _texcoord( "", 2D ) = "white" {}
	}

	Category 
	{
		SubShader
		{
			Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
			Blend One[_Blend2]
			ColorMask RGB
			Cull[_CullMode]
			Lighting Off 
			ZWrite Off
			ZTest LEqual
			
			Pass 
			{
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 2.0
				#pragma multi_compile_particles
				#pragma multi_compile_fog

				#include "UnityShaderVariables.cginc"
				#include "UnityCG.cginc"

				struct appdata_t 
				{
					float4 vertex : POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f 
				{
					float4 vertex : SV_POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_FOG_COORDS(1)
					#ifdef SOFTPARTICLES_ON
						float4 projPos : TEXCOORD2;
					#endif
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO			
				};	
				
				#if UNITY_VERSION >= 560
					UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
				#else
					uniform sampler2D_float _CameraDepthTexture;
				#endif

				uniform sampler2D _MainTex;
				uniform float4 _MainTex_ST;

				uniform sampler2D _Flow;
				uniform float4 _Flow_ST;

				uniform sampler2D _Mask;
				uniform float4 _Mask_ST;

				uniform sampler2D _Noise;
				uniform float4 _Noise_ST;

				uniform float4 _SpeedMainTexUVNoiseZW;
				uniform float4 _DistortionSpeedXYPowerZ;

				// ===== 新增 Noise Flow =====
				uniform float _UseNoiseFlow;
				uniform float4 _NoiseFlowSpeed;

				uniform float4 _Color;
				uniform float _Emission;
				uniform fixed _Usedepth;
				uniform float _Depthpower;
				uniform float _Usecenterglow;

				v2f vert (appdata_t v)
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);

					o.vertex = UnityObjectToClipPos(v.vertex);

					#ifdef SOFTPARTICLES_ON
						o.projPos = ComputeScreenPos(o.vertex);
						COMPUTE_EYEDEPTH(o.projPos.z);
					#endif

					o.color = v.color;
					o.texcoord = v.texcoord;
					UNITY_TRANSFER_FOG(o,o.vertex);
					return o;
				}

				fixed4 frag (v2f i) : SV_Target
				{
					float lp = 1;

					#ifdef SOFTPARTICLES_ON
						float sceneZ = LinearEyeDepth(
							SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos))
						);
						float partZ = i.projPos.z;
						float fade = saturate((sceneZ - partZ) / _Depthpower);
						lp *= lerp(1, fade, _Usedepth);
						i.color.a *= lp;
					#endif

					// ===== MainTex 流动 =====
					float2 mainSpeed = _SpeedMainTexUVNoiseZW.xy;
					float2 uvMain = i.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
					float2 mainUV = uvMain + _Time.y * mainSpeed;

					// ===== Flow 扰动 =====
					float2 flowSpeed = _DistortionSpeedXYPowerZ.xy;
					float flowPower = _DistortionSpeedXYPowerZ.z;

					float2 uvFlow = i.texcoord.xy * _Flow_ST.xy + _Flow_ST.zw;
					float2 flowUV = uvFlow + _Time.y * flowSpeed;

					float4 flowTex = tex2D(_Flow, flowUV);
					float4 maskTex = tex2D(_Mask, i.texcoord.xy * _Mask_ST.xy + _Mask_ST.zw);

					mainUV -= (flowTex * maskTex).rg * flowPower;

					float4 mainTex = tex2D(_MainTex, mainUV);

					// ===== Noise（新增独立流动）=====
					float2 uvNoise = i.texcoord.xy * _Noise_ST.xy + _Noise_ST.zw;
					float2 noiseOffset = _Time.y * _NoiseFlowSpeed.xy * _UseNoiseFlow;
					float4 noiseTex = tex2D(_Noise, uvNoise + noiseOffset);

					// ===== 颜色合成 =====
					float4 baseCol =
						mainTex *
						noiseTex *
						_Color *
						i.color *
						mainTex.a *
						noiseTex.a *
						_Color.a *
						i.color.a;

					// ===== Center Glow =====
					float centerMask = saturate(maskTex.r - (1.0 - i.texcoord.z));
					float4 glowCol = baseCol * centerMask;

					fixed4 col = lerp(baseCol, glowCol, _Usecenterglow) * _Emission;

					UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0,0,0,1));
					return col;
				}
				ENDCG
			}
		}
	}
}
