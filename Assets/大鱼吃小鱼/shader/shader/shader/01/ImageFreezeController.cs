using UnityEngine;
using System.Collections;

// 强制要求物体上有 Renderer 组件 (如 MeshRenderer，用于显示图片的 Quad 就有这个)
[RequireComponent(typeof(Renderer))]
public class ImageFreezeController : MonoBehaviour
{
    [Header("Freeze Settings")]
    [Tooltip("冰封过程从 0% 到 100% 所需时间 (秒)")]
    public float freezeDuration = 3f;

    [Header("Testing Slot (Optional)")]
    [Tooltip("把你想测试的图片拖到这里，然后用右键菜单测试切换")]
    public Texture2D testImageSlot;

    // 材质实例的引用
    private Material mat;
    
    // Shader 属性 ID 缓存，提升运行效率
    private int freezeAmountID;
    private int mainTexID;
    
    private Coroutine currentRoutine;

    void Start()
    {
        // 获取 Renderer 并自动实例化材质，确保修改互不影响
        mat = GetComponent<Renderer>().material;

        // 缓存 Shader 属性 ID
        freezeAmountID = Shader.PropertyToID("_FreezeAmount");
        mainTexID = Shader.PropertyToID("_MainTex");

        // 初始化：确保开始时是完全解冻状态
        mat.SetFloat(freezeAmountID, 0f);

        // 初始化时根据当前材质的贴图调整一次比例
        if (mat.mainTexture != null)
        {
            AdjustAspectRatio(mat.mainTexture);
        }
    }

    //================================================
    // 公开 API - 供其他脚本调用
    //================================================

    /// <summary>
    /// 【核心功能】运行时动态更换底图（那张“任意图片”）
    /// </summary>
    /// <param name="newImage">新的图片纹理</param>
    public void SetBaseImage(Texture newImage)
    {
        if (mat == null || newImage == null) return;
        
        // 设置材质的主贴图
        mat.SetTexture(mainTexID, newImage);
        
        // 关键一步：根据新图片的尺寸调整物体的宽高比，防止变形
        AdjustAspectRatio(newImage);
        
        Debug.Log($"[ImageFreezeController] 底图已更换为: {newImage.name}");
    }

    /// <summary>
    /// 开始执行冰封动画
    /// </summary>
    public void StartFreezing() => TriggerAnimation(1f);

    /// <summary>
    /// 开始执行解冻动画
    /// </summary>
    public void StartUnfreezing() => TriggerAnimation(0f);


    //================================================
    // 内部逻辑
    //================================================

    private void TriggerAnimation(float targetValue)
    {
        if (mat == null) return;
        if (currentRoutine != null) StopCoroutine(currentRoutine);
        currentRoutine = StartCoroutine(AnimateFreeze(mat.GetFloat(freezeAmountID), targetValue));
    }

    // 根据图片纹理的宽高比，调整自身 transform 的缩放
    private void AdjustAspectRatio(Texture tex)
    {
        if (tex == null || tex.height == 0) return;
        float aspectRatio = (float)tex.width / tex.height;
        // 保持 Y 轴缩放为 1，根据比例调整 X 轴缩放
        transform.localScale = new Vector3(aspectRatio, 1f, 1f);
    }

    private IEnumerator AnimateFreeze(float startVal, float endVal)
    {
        float elapsed = 0f;
        while (elapsed < freezeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / freezeDuration); 
            mat.SetFloat(freezeAmountID, Mathf.Lerp(startVal, endVal, t));
            yield return null;
        }
        mat.SetFloat(freezeAmountID, endVal);
        currentRoutine = null;
    }

    //================================================
    // 编辑器右键菜单测试项
    //================================================
    [ContextMenu("测试：开始冰封")] private void TestFreeze() => StartFreezing();
    [ContextMenu("测试：开始解冻")] private void TestUnfreeze() => StartUnfreezing();
    [ContextMenu("测试：更换为 Test Slot 中的图片")]
    private void TestSwapImage()
    {
        if (testImageSlot != null) SetBaseImage(testImageSlot);
        else Debug.LogWarning("请先在 Inspector 的 Test Image Slot 中放入一张图片。");
    }
}