using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class UnderwaterScreenEffect : MonoBehaviour
{
    [Header("核心材质")]
    public Material underwaterMaterial;

    [Header("海洋环境设置")]
    public float waterLevel = 0.0f;                              // 海平面高度(摄像机低于此高度开启特效)
    public Color waterColor = new Color(0.0f, 0.4f, 0.7f, 1.0f); // 水下雾气颜色
    public float fogDensity = 0.04f;                             // 水下能见度/雾浓度

    [Header("顶部屏幕扭曲控制 (Noise)")]
    public Texture2D noiseTexture;                               // 扭曲用的黑白贴图
    [Range(0f, 0.1f)] public float distortionIntensity = 0.02f;  // 扭曲强度
    [Range(0f, 1f)] public float distortAreaTop = 0.5f;          // 遮罩：0.5代表屏幕上半部分扭曲，下半部分清晰
    
    [Header("扭曲贴图 UV 控制")]
    public Vector2 noiseScale = new Vector2(1f, 1f);             // UV缩放 (改小可以拉长水波)
    public Vector2 noiseSpeed = new Vector2(0.1f, 0.05f);        // UV位移速度 (控制水波流向)

    private Camera cam;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        // 开启深度图，雾效依然需要它
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        // 判断是否在水下
        if (underwaterMaterial != null && cam.transform.position.y < waterLevel)
        {
            underwaterMaterial.SetColor("_WaterColor", waterColor);
            underwaterMaterial.SetFloat("_FogDensity", fogDensity);

            // 传递扭曲参数
            if (noiseTexture != null) underwaterMaterial.SetTexture("_NoiseTex", noiseTexture);
            underwaterMaterial.SetFloat("_DistortIntensity", distortionIntensity);
            underwaterMaterial.SetFloat("_DistortArea", distortAreaTop);
            
            // 传递 Vector 2 缩放和流动速度
            underwaterMaterial.SetVector("_NoiseScale", noiseScale);
            underwaterMaterial.SetVector("_NoiseSpeed", noiseSpeed);

            Graphics.Blit(src, dest, underwaterMaterial);
        }
        else
        {
            // 摄像机在水面上，正常渲染
            Graphics.Blit(src, dest);
        }
    }
}