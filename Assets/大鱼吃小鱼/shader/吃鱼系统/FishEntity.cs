using UnityEngine;

public class FishEntity : MonoBehaviour
{
    [Header("基础属性")]
    public bool isPlayer = false;
    public float initialWeight = 10f;
    public float CurrentWeight { get; private set; }

    [Header("组件引用")]
    public Transform visualRoot; // 挂载SpriteRenderer的子节点
    public Collider2D bodyCollider; // 被吃判定体

    private float _targetScaleFactor = 1.0f;
    private float _scaleVelocity;

    private void Start()
    {
        CurrentWeight = initialWeight;
        // 初始计算体型缩放目标值 (使用立方根让生长更自然)
        _targetScaleFactor = Mathf.Pow(CurrentWeight / 10f, 1f / 3f);
    }

    private void Update()
    {
        // 只有体型平滑变大逻辑，摄像机完全不再受此脚本控制
        float currentScale = visualRoot.localScale.x;
        float newScale = Mathf.SmoothDamp(currentScale, _targetScaleFactor, ref _scaleVelocity, 0.3f);
        visualRoot.localScale = new Vector3(newScale, newScale, 1f);
    }

    // 判断是否能吃掉对方 (比对方大10%即可)
    public bool CanEat(FishEntity prey)
    {
        return this.CurrentWeight > prey.CurrentWeight * 1.1f;
    }

    // 增加体重并重新计算目标缩放值
    public void AddWeight(float weightGained)
    {
        CurrentWeight += weightGained;
        _targetScaleFactor = Mathf.Pow(CurrentWeight / 10f, 1f / 3f);
    }

    // 被吃时的逻辑
    public void OnBeEaten()
    {
        bodyCollider.enabled = false; // 关闭自身的物理碰撞
        
        // 避开尖括号报错，使用 typeof 安全获取并禁用移动组件
        FishMovement movement = GetComponent(typeof(FishMovement)) as FishMovement;
        if (movement != null) movement.enabled = false;
    }

    // 回收/销毁
    public void Recycle()
    {
        Destroy(gameObject); 
    }
}