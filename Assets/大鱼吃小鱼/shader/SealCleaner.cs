using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 海豹清理插件：支持多属性 PBR 混合、透明油污以及编辑器模式实时预览
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshCollider))]
public class SealCleaner : MonoBehaviour
{
    [Header("渲染组件")]
    public Renderer sealRenderer;
    public int textureSize = 1024;

    [Header("清理设置")]
    [Range(0.01f, 0.5f)]
    public float brushSize = 0.05f;
    public Texture2D brushShape;

    private Material targetMat;
    private RenderTexture maskRT;
    private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

    void OnEnable()
    {
        Initialize();
    }

    void Initialize()
    {
        if (sealRenderer == null) sealRenderer = GetComponent<Renderer>();
        if (sealRenderer == null) return;

        // 使用 sharedMaterial 在编辑器下预览，运行模式会自动实例
        targetMat = Application.isPlaying ? sealRenderer.material : sealRenderer.sharedMaterial;

        if (targetMat == null || !targetMat.HasProperty(MaskTexId)) return;

        // 重新创建 RT 防止丢失
        if (maskRT == null || maskRT.width != textureSize)
        {
            if (maskRT != null) maskRT.Release();
            maskRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.R8);
            maskRT.filterMode = FilterMode.Bilinear;
            maskRT.wrapMode = TextureWrapMode.Clamp;
            maskRT.hideFlags = HideFlags.DontSave;
            maskRT.Create();
            
            // 初始化为全油污（白色）
            Graphics.Blit(Texture2D.whiteTexture, maskRT);
        }

        targetMat.SetTexture(MaskTexId, maskRT);
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            if (Input.GetMouseButton(0))
            {
                HandleInteraction(Input.mousePosition);
            }
        }
#if UNITY_EDITOR
        else
        {
            // 编辑器模式下的点击处理
            HandleEditorInteraction();
        }
#endif
    }

#if UNITY_EDITOR
    private void HandleEditorInteraction()
    {
        Event e = Event.current;
        if (e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
        {
            // 编辑器下需要将鼠标位置转换为 SceneView 射线
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            CheckRaycast(ray);
        }
    }
#endif

    private void HandleInteraction(Vector3 mousePos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(mousePos);
        CheckRaycast(ray);
    }

    private void CheckRaycast(Ray ray)
    {
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider != null && hit.collider.transform == transform)
            {
                UpdateMask(hit.textureCoord);
#if UNITY_EDITOR
                if (!Application.isPlaying) EditorUtility.SetDirty(this);
#endif
            }
        }
    }

    void UpdateMask(Vector2 uv)
    {
        if (maskRT == null || !maskRT.IsCreated()) Initialize();

        RenderTexture.active = maskRT;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, 1, 1, 0);

        Rect rect = new Rect(uv.x - brushSize / 2, (1 - uv.y) - brushSize / 2, brushSize, brushSize);

        if (brushShape != null)
            Graphics.DrawTexture(rect, brushShape);
        else
            Graphics.DrawTexture(rect, Texture2D.blackTexture);

        GL.PopMatrix();
        RenderTexture.active = null;
    }

    private void OnDisable()
    {
        // 只有在真正销毁时才释放，防止编辑器切换导致RT闪烁
        if (!Application.isPlaying && maskRT != null)
        {
            // 不要在编辑器模式下随便 Release RT，否则预览会变黑
        }
    }

    private void OnDestroy()
    {
        if (maskRT != null)
        {
            maskRT.Release();
            DestroyImmediate(maskRT);
        }
    }
}