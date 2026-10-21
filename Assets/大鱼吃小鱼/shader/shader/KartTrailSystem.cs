using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 卡丁车拖尾系统 (Kart Trail System)
/// 适用于 Unity 2019 标准管线。
/// 功能：父物体带动路径上的子物体有序运动，形成拖尾效果，并可绘制路径线，支持路径发光粒子。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class KartTrailSystem : MonoBehaviour
{
    [Header("Trail Settings (拖尾设置)")]
    [Tooltip("拖尾子物体的预制体或实例 (Trail child prefab or instance)")]
    public GameObject trailChildPrefab;

    [Tooltip("子物体的数量 (Number of child objects)")]
    [Range(1, 50)]
    public int childCount = 10;

    [Tooltip("每个子物体之间的路径距离 (Distance between each child object on the path)")]
    public float distanceBetweenChildren = 1.0f;

    [Header("Path Settings (路径设置)")]
    [Tooltip("记录路径点的最小距离间隔 (Minimum distance interval to record a path point)")]
    public float recordDistanceInterval = 0.2f;

    [Tooltip("保存的最大路径点数量 (防止内存无限增长)")]
    public int maxPathPoints = 500;

    [Header("Particle Settings (粒子设置)")]
    [Tooltip("是否启用路径上的发光粒子 (Enable glowing particles on the path)")]
    public bool enableParticles = true;

    [Tooltip("用于在路径上发光的粒子系统，建议设置为 World Space (Particle system for the path, recommend World Space)")]
    public ParticleSystem pathParticleSystem;

    [Tooltip("粒子生成的距离间隔 (Distance interval to emit each particle)")]
    public float particleInterval = 0.5f;

    [Header("Line Renderer Settings (线条设置)")]
    [Tooltip("路径线条的宽度 (Width of the path line)")]
    public float lineWidth = 0.5f;
    
    [Tooltip("是否显示线条 (Whether to show the LineRenderer)")]
    public bool showLine = true;

    [Tooltip("是否平铺纹理以防止拉伸 (Tile texture to prevent UV stretching)")]
    public bool tileTexture = true;

    // 存储历史路径点的列表
    private List<Vector3> pathPoints = new List<Vector3>();
    
    // 存储实例化的子物体列表
    private List<GameObject> activeChildren = new List<GameObject>();
    
    // LineRenderer 组件引用
    private LineRenderer lineRenderer;
    
    // 记录上一次保存路径点的位置
    private Vector3 lastRecordedPosition;

    // 记录上一次生成粒子的位置
    private Vector3 lastParticlePosition;

    void Awake()
    {
        // 获取或添加 LineRenderer 组件
        lineRenderer = GetComponent<LineRenderer>();
        
        // 初始化 LineRenderer 设置
        SetupLineRenderer();
        
        // 记录初始位置
        lastRecordedPosition = transform.position;
        lastParticlePosition = transform.position;
        pathPoints.Add(lastRecordedPosition);
    }

    void Start()
    {
        InitializeChildren();
    }

    private void SetupLineRenderer()
    {
        if (lineRenderer == null) return;

        // 设置线条宽度
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        
        // 使用世界坐标空间
        lineRenderer.useWorldSpace = true;
        
        // 初始化时不显示任何点
        lineRenderer.positionCount = 0;
        
        // 确保启用状态与设置一致
        lineRenderer.enabled = showLine;
        
        // 设置 UV 平铺模式，防止纹理随着线条变长而无限拉伸
        lineRenderer.textureMode = tileTexture ? LineTextureMode.Tile : LineTextureMode.Stretch;
        
        // 材质在外部设置，这里不作处理
    }

    private void InitializeChildren()
    {
        if (trailChildPrefab == null)
        {
            Debug.LogWarning("KartTrailSystem: 没有指定子物体预制体 (Trail Child Prefab is not assigned)!");
            return;
        }

        // 清理可能已有的子物体
        foreach (var child in activeChildren)
        {
            if (child != null) Destroy(child);
        }
        activeChildren.Clear();

        // 实例化指定数量的子物体
        for (int i = 0; i < childCount; i++)
        {
            GameObject newChild = Instantiate(trailChildPrefab, transform.position, Quaternion.identity);
            
            // 可以选择将其作为子物体，但为了自由跟随路径，最好放在根目录或专门的容器中
            // 为了保持层级整洁，我们将其命名并让其独立在场景中
            newChild.name = $"TrailChild_{i}";
            
            // 如果你希望它们在层级面板中收纳在父物体下，可以取消下面这行的注释
            // 注意：这可能会影响某些基于世界坐标的计算，但这里的算法是基于世界路径点的，所以没关系
            // newChild.transform.SetParent(this.transform); 
            
            activeChildren.Add(newChild);
        }
    }

    void Update()
    {
        // 1. 记录路径点
        RecordPath();

        // 2. 在路径上生成粒子
        EmitPathParticles();

        // 3. 更新子物体位置
        UpdateChildrenPositions();

        // 4. 更新线条显示
        UpdateLineRenderer();
    }

    private void RecordPath()
    {
        // 计算当前位置与上一次记录位置的距离
        float distanceSinceLastRecord = Vector3.Distance(transform.position, lastRecordedPosition);

        // 如果移动距离超过设定的间隔，则记录新的路径点
        if (distanceSinceLastRecord >= recordDistanceInterval)
        {
            pathPoints.Insert(0, transform.position); // 将新点插入到列表头部 (索引0是最新的点)
            lastRecordedPosition = transform.position;

            // 限制路径点列表的最大长度，防止内存泄漏
            if (pathPoints.Count > maxPathPoints)
            {
                // 移除最旧的点
                pathPoints.RemoveAt(pathPoints.Count - 1);
            }
        }
    }

    private void EmitPathParticles()
    {
        if (!enableParticles || pathParticleSystem == null) return;
        if (particleInterval <= 0.01f) return; // 防止间隔过小导致死循环 (Prevent infinite loop)

        // 计算当前位置与上一次生成粒子位置的距离
        float distanceSinceLastParticle = Vector3.Distance(transform.position, lastParticlePosition);

        // 如果距离超过了设定的粒子间隔
        if (distanceSinceLastParticle >= particleInterval)
        {
            // 计算需要发射几个粒子（防止掉帧或速度过快时出现粒子断层）
            int emitCount = Mathf.FloorToInt(distanceSinceLastParticle / particleInterval);
            Vector3 direction = (transform.position - lastParticlePosition).normalized;

            for (int i = 1; i <= emitCount; i++)
            {
                // 精确计算每个粒子的生成位置
                Vector3 spawnPos = lastParticlePosition + direction * (particleInterval * i);
                
                // 设置粒子参数并手动发射
                ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
                emitParams.position = spawnPos;
                
                // 发射1个粒子
                pathParticleSystem.Emit(emitParams, 1);
            }

            // 更新上一次生成粒子的位置，确保粒子间距严格保持一致
            lastParticlePosition += direction * (emitCount * particleInterval);
        }
    }

    private void UpdateChildrenPositions()
    {
        if (activeChildren.Count == 0 || pathPoints.Count < 2) return;

        // 遍历所有子物体，计算它们在路径上的位置
        for (int i = 0; i < activeChildren.Count; i++)
        {
            GameObject child = activeChildren[i];
            if (child == null) continue;

            // 第 i 个子物体应该在路径上距离车头 (i+1) * distanceBetweenChildren 的位置
            float targetDistance = (i + 1) * distanceBetweenChildren;
            
            // 获取该距离在路径上的确切位置和旋转
            Vector3 targetPos = GetPositionAtDistance(targetDistance, out Quaternion targetRot);

            // 更新子物体的 Transform
            child.transform.position = targetPos;
            
            // 如果需要子物体也跟随路径的朝向，可以取消注释下面这行
            child.transform.rotation = targetRot; 
        }
    }

    /// <summary>
    /// 根据给定的距离，在历史路径点中插值计算出具体的位置和旋转。
    /// </summary>
    private Vector3 GetPositionAtDistance(float targetDistance, out Quaternion rotation)
    {
        float currentDistance = 0f;
        rotation = Quaternion.identity;

        // 默认返回最旧的那个点（如果路径不够长）
        if (pathPoints.Count == 0) return transform.position;
        if (pathPoints.Count == 1) return pathPoints[0];

        // 遍历路径点（从最新到最旧）累加距离
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector3 p1 = pathPoints[i];
            Vector3 p2 = pathPoints[i + 1];
            float segmentLength = Vector3.Distance(p1, p2);

            // 如果目标距离落在这个线段内
            if (currentDistance + segmentLength >= targetDistance)
            {
                // 计算在这个线段上的插值比例
                float t = (targetDistance - currentDistance) / segmentLength;
                
                // 计算朝向 (从旧点指向新点)
                Vector3 direction = (p1 - p2).normalized;
                if (direction != Vector3.zero)
                {
                    rotation = Quaternion.LookRotation(direction);
                }

                // 返回插值位置
                return Vector3.Lerp(p1, p2, t);
            }

            // 累加已走过的路径长度
            currentDistance += segmentLength;
        }

        // 如果历史路径总长度小于目标距离，则将子物体停留在路径的最后一个点（最旧的点）
        // 并且计算最后一段的朝向
        Vector3 lastDir = (pathPoints[pathPoints.Count - 2] - pathPoints[pathPoints.Count - 1]).normalized;
        if(lastDir != Vector3.zero) rotation = Quaternion.LookRotation(lastDir);
        
        return pathPoints[pathPoints.Count - 1];
    }

    private void UpdateLineRenderer()
    {
        if (lineRenderer == null) return;
        
        lineRenderer.enabled = showLine;
        
        if (!showLine) return;

        // 动态更新线条宽度 (以防在运行时修改了 inspector 里的值)
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;

        // 将路径点赋予 LineRenderer
        lineRenderer.positionCount = pathPoints.Count;
        lineRenderer.SetPositions(pathPoints.ToArray());
    }
    
    void OnValidate()
    {
        // 当在 Inspector 中修改参数时，尝试更新 LineRenderer (仅在运行时有效，或者配合 ExecuteInEditMode)
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.enabled = showLine;
            lineRenderer.textureMode = tileTexture ? LineTextureMode.Tile : LineTextureMode.Stretch;
        }
    }
}