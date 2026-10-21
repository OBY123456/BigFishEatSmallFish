using UnityEngine;
using Lean.Pool;

// 保留旧版Prefab序列化中的枚举数值；漩涡重新加入，但放在Items数组的第5个槽位。
public enum ItemType
{
    SpikeShield = 0,
    Vortex = 1,
    HuntingOrder = 2,
    SlowTime = 3,
    Invisibility = 4
}

public class ItemBase : MonoBehaviour
{
    [Header("道具类型：护盾、漩涡、猎食令、缓速、隐身")]
    public ItemType type;

    [Header("尖刺护盾的无敌和接触扣分持续时间，秒")]
    [Min(0f)] public float shieldDuration = 10f;

    [Header("未能取得鱼身体Sprite尺寸时，漩涡吸附半径使用的后备值")]
    [Min(0.1f)] public float vortexRadius = 2f;
    [Header("漩涡道具装备到鱼身上时的局部缩放，默认(0.4,0.4,0)")]
    public Vector3 equippedVortexLocalScale = new Vector3(0.4f, 0.4f, 0f);
    [Header("漩涡道具存在和吸附鱼的时间，秒")]
    [Min(0.1f)] public float vortexDuration = 10f;

    [Header("猎食令可以吞噬低等级玩家的时间，秒")]
    [Min(0f)] public float huntingDuration = 10f;

    [Header("时间缓速影响周围鱼群的范围半径，世界单位")]
    [Min(0.1f)] public float slowRadius = 3f;
    [Header("时间缓速的持续时间，秒；范围内其他鱼降低40%速度")]
    [Min(0f)] public float slowDuration = 5f;

    [Header("隐身并无敌的持续时间，秒")]
    [Min(0f)] public float invisibilityDuration = 20f;

    private Vector3 originalLocalScale;
    private Quaternion originalLocalRotation;
    private Collider2D[] itemColliders;
    private bool[] originalColliderEnabled;
    private Rigidbody2D[] itemRigidbodies;
    private bool[] originalRigidbodySimulated;
    private Renderer[] itemRenderers;
    private int[] originalSortingLayerIds;
    private int[] originalSortingOrders;
    private ParticleSystem[] itemParticleSystems;
    private ParticleSystemSimulationSpace[] originalParticleSimulationSpaces;
    private bool isEquippedVortex;

    public bool IsEquippedVortex { get { return isEquippedVortex; } }

    private void Awake()
    {
        // 记录道具在Prefab中的样子，供LeanPool下一次刷新时恢复。
        originalLocalScale = transform.localScale;
        originalLocalRotation = transform.localRotation;
        itemColliders = GetComponentsInChildren<Collider2D>(true);
        originalColliderEnabled = new bool[itemColliders.Length];
        for (int i = 0; i < itemColliders.Length; i++)
            originalColliderEnabled[i] = itemColliders[i] != null && itemColliders[i].enabled;

        itemRigidbodies = GetComponentsInChildren<Rigidbody2D>(true);
        originalRigidbodySimulated = new bool[itemRigidbodies.Length];
        for (int i = 0; i < itemRigidbodies.Length; i++)
            originalRigidbodySimulated[i] = itemRigidbodies[i] != null && itemRigidbodies[i].simulated;

        itemRenderers = GetComponentsInChildren<Renderer>(true);
        originalSortingLayerIds = new int[itemRenderers.Length];
        originalSortingOrders = new int[itemRenderers.Length];
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            if (itemRenderers[i] == null) continue;
            originalSortingLayerIds[i] = itemRenderers[i].sortingLayerID;
            originalSortingOrders[i] = itemRenderers[i].sortingOrder;
        }

        itemParticleSystems = GetComponentsInChildren<ParticleSystem>(true);
        originalParticleSimulationSpaces = new ParticleSystemSimulationSpace[itemParticleSystems.Length];
        for (int i = 0; i < itemParticleSystems.Length; i++)
            originalParticleSimulationSpaces[i] = itemParticleSystems[i].main.simulationSpace;
    }

    private void OnEnable()
    {
        // 已装备在鱼身上的漩涡，父鱼重新激活时仍然保持触发器关闭。
        // 真正从LeanPool重新生成道具时，由FishManager.SpawnItem()
        // 明确调用PrepareForWorldSpawn()来恢复原始Collider2D状态。
        if (isEquippedVortex) return;
        PrepareForWorldSpawn();
    }

    public void PrepareForWorldSpawn()
    {
        isEquippedVortex = false;
        transform.localScale = originalLocalScale;
        transform.localRotation = originalLocalRotation;

        if (itemColliders != null)
        {
            for (int i = 0; i < itemColliders.Length; i++)
                if (itemColliders[i] != null) itemColliders[i].enabled = originalColliderEnabled[i];
        }

        if (itemRigidbodies != null)
        {
            for (int i = 0; i < itemRigidbodies.Length; i++)
                if (itemRigidbodies[i] != null) itemRigidbodies[i].simulated = originalRigidbodySimulated[i];
        }

        if (itemRenderers != null)
        {
            for (int i = 0; i < itemRenderers.Length; i++)
            {
                if (itemRenderers[i] == null) continue;
                itemRenderers[i].sortingLayerID = originalSortingLayerIds[i];
                itemRenderers[i].sortingOrder = originalSortingOrders[i];
            }
        }

        if (itemParticleSystems != null)
        {
            for (int i = 0; i < itemParticleSystems.Length; i++)
            {
                if (itemParticleSystems[i] == null) continue;
                ParticleSystem.MainModule main = itemParticleSystems[i].main;
                main.simulationSpace = originalParticleSimulationSpaces[i];
            }
        }
    }

    public bool MarkAsEquippedVortex()
    {
        if (isEquippedVortex || type != ItemType.Vortex) return false;

        isEquippedVortex = true;
        // 道具已从可拾取物变成鱼身上的漩涡，不允许自己再触发拾取。
        if (itemColliders != null)
        {
            for (int i = 0; i < itemColliders.Length; i++)
                if (itemColliders[i] != null) itemColliders[i].enabled = false;
        }

        if (itemRigidbodies != null)
        {
            for (int i = 0; i < itemRigidbodies.Length; i++)
                if (itemRigidbodies[i] != null) itemRigidbodies[i].simulated = false;
        }

        // 即使原粒子使用World模拟空间，装备期间也改成Local，
        // 否则变成子物体后，已有粒子仍停留在原来的世界位置，看上去像没有跟随鱼。
        if (itemParticleSystems != null)
        {
            for (int i = 0; i < itemParticleSystems.Length; i++)
            {
                if (itemParticleSystems[i] == null) continue;
                ParticleSystem.MainModule main = itemParticleSystems[i].main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
        }

        return true;
    }

    public void UseItem(FishCore fish)
    {
        if (fish == null || fish.isDead || isEquippedVortex) return;

        switch (type)
        {
            case ItemType.SpikeShield:
                fish.ApplyShield(shieldDuration);
                break;
            case ItemType.Vortex:
                // 直接把刚拾取的道具本体挂到鱼下面；不能先回收道具，再生成另一份特效。
                if (FishManager.Instance != null &&
                    FishManager.Instance.EquipVortexItem(fish, this, vortexDuration, vortexRadius))
                    return;
                // 装备失败时仍留在原位置，不消耗道具。
                return;
            case ItemType.HuntingOrder:
                fish.ApplyHuntingOrder(huntingDuration);
                break;
            case ItemType.SlowTime:
                if (FishManager.Instance != null)
                    FishManager.Instance.ApplySlowTime(fish, 0.6f, slowDuration, slowRadius);
                break;
            case ItemType.Invisibility:
                fish.ApplyInvisibility(invisibilityDuration);
                break;
            default:
                Debug.LogWarning("尚未支持的道具类型：" + type);
                return;
        }

        if (FishManager.Instance != null) FishManager.Instance.NotifyItemDespawned(gameObject);
        LeanPool.Despawn(gameObject);
    }
}
