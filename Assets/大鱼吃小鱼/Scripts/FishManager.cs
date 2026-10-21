using UnityEngine;
using Lean.Pool;
using MqttData;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

public class FishManager : MonoBehaviour
{
    public static FishManager Instance;

    [Header("所有鱼的1级基础游动速度，其他等级在此速度上按比例递增")]
    public float globalBaseSpeed = 5f;
    [Header("每提高1级的基础移速增长比例：0.1代表每级增加10%，2级为1.1倍、3级为1.2倍")]
    [Min(0f)] public float levelSpeedIncreasePerLevel = 0.1f;
    [Header("玩家进场后的普通NPC目标数量（无人时约一半）")]
    public int initialNpcCount = 20;
    [Header("普通特殊鱼及鲨鱼在场的基础数量上限，随平均等级增加")]
    [Min(0)] public int initialSpecialCount = 3;
    [Header("每次NPC刷新时尝试生成特殊鱼的基础概率（0.06=6%）")]
    [Range(0f, 1f)] public float specialFishBaseChance = 0.06f;
    [Header("每名在场玩家给特殊鱼增加的概率（0.006=0.6%）")]
    [Range(0f, 0.1f)] public float specialFishChancePerPlayer = 0.006f;
    [Header("玩家平均等级每提高一级，额外允许存在的特殊鱼数量")]
    [Min(0f)] public float specialExtraPerAverageLevel = 0.4f;
    [Header("闪电鱼被吃后给玩家的2.5倍加速持续时间，秒")]
    [Min(0f)] public float lightningBuffDuration = 10f;
    [Header("巨人鱼被吃后玩家临时提升一级持续时间，秒")]
    [Min(0f)] public float giantBonusDuration = 10f;
    [Header("鲨鱼在正常游动时使用的速度倍率")]
    [Min(1f)] public float sharkSpeedMultiplier = 1.3f;
    [Tooltip("无人时先生成 initialNpcCount 的一半；若种类更多则保证每种至少一条")]
    [Header("无人时是否保证每种已配置NPC至少有一条")]
    public bool keepEveryNpcTypeVisible = true;
    [Header("检查并补充普通NPC鱼的间隔秒数")]
    [Min(0.2f)] public float npcPopulationCheckInterval = 2f;
    [Header("所有运行时鱼、道具、漩涡的父物体")]
    public Transform entityRoot;
    [Header("用于透视投影计算鱼活动范围的摄像机")]
    public Camera mainCamera;

    [Header("是否以实体根节点的世界Z坐标作为鱼平面")]
    public bool useEntityRootZ = true;
    [Header("未使用实体根节点Z时的鱼所在世界Z平面")]
    public float fishPlaneZ = 0f;
    [Header("鱼在屏幕四周保留的投影范围百分比")]
    [Range(0f, 0.2f)] public float screenEdgePadding = 0.02f;
    [Tooltip("下边界偏移：正数向上收缩，负数向下扩展")]
    [Header("鱼活动下边界相对相机计算值向上调整的距离")]
    public float bottomBoundaryOffset = 0f;
    [Tooltip("上边界偏移：正数向下收缩，负数向上扩展")]
    [Header("鱼活动上边界相对相机计算值向下调整的距离")]
    public float topBoundaryOffset = 0f;

    [Header("在编辑器中按此键创建本地键盘调试鱼")]
    public KeyCode localPlayerSpawnKey = KeyCode.F8;
    [Header("玩家出生或复活后无敌持续秒数")]
    public float playerSpawnInvincibleSeconds = 3f;
    [Header("键盘空格和MQTT加速时的速度倍率")]
    public float localBoostMultiplier = 1.8f;

    private float ActiveFishPlaneZ
    {
        get { return useEntityRootZ && entityRoot != null ? entityRoot.position.z : fishPlaneZ; }
    }

    private int lastCameraWidth;
    private int lastCameraHeight;
    private float lastCameraAspect;
    private float lastCameraFov;
    private float lastCameraSize;
    private float lastFishPlaneZ;
    private float lastBottomBoundaryOffset;
    private float lastTopBoundaryOffset;
    private float lastScreenEdgePadding;
    private Vector3 lastCameraPosition;
    private Quaternion lastCameraRotation;

    [Header("普通NPC鱼预制体数组，按等级顺序配置")]
    public GameObject[] NPCFish = new GameObject[9];
    [Header("玩家鱼预制体数组，0号为玩家初始鱼")]
    public GameObject[] PlayerFish = new GameObject[10];
    [Tooltip("顺序：0闪电鱼、1黄金鱼、2巨人鱼、3鲨鱼")]
    [Header("特殊鱼预制体，0闪电1黄金2巨人3鲨鱼")]
    public GameObject[] SpecialFish = new GameObject[4];
    [Tooltip("数组顺序：0护盾、1猎食令、2缓速、3隐身、4漩涡。拾取漩涡后直接装备 Items[4] 的实例，不生成额外特效。")]
    [Header("五种道具Prefab：0护盾1猎食令2缓速3隐身4漩涡")]
    public GameObject[] Items = new GameObject[5];

    [Header("首次创建及低平均等级时的道具基础数量")]
    [Min(0)] public int initialItemCount = 4;
    [Header("每次道具刷新判定的基础概率（0.04=4%）")]
    [Range(0f, 1f)] public float itemBaseChance = 0.04f;
    [Header("每名玩家给道具刷新增加的概率（0.004=0.4%）")]
    [Range(0f, 0.1f)] public float itemChancePerPlayer = 0.004f;
    [Header("玩家平均等级每提高一级，可额外出现的道具数量")]
    [Min(0f)] public float itemExtraPerAverageLevel = 0.5f;
    [Header("场景同时存在的道具最大数量")]
    [Min(0)] public int maxItemCount = 8;
    [Header("道具被拾取后补充道具的检查间隔，单位秒")]
    [Min(0.2f)] public float itemRespawnInterval = 8f;
    [Header("道具出生的中心区域比例（0.6表示屏幕中央60%宽高）")]
    [Range(0.1f, 1f)] public float itemSpawnCenterRatio = 0.6f;
    [Header("两件道具之间的最小中心距离，世界单位")]
    [Min(0.05f)] public float itemMinSpacing = 0.8f;
    [Header("道具与漩涡之间的最小中心距离，世界单位")]
    [Min(0f)] public float itemVortexSpacing = 0.8f;
    [Header("单次寻找道具空位的最大随机尝试次数")]
    [Min(1)] public int itemPlacementAttempts = 50;

    [Header("漩涡风暴使用的特效预制体；建议拖入独立的 ParticleSystem Prefab")]
    [Tooltip("没有填写时自动创建占位旋涡图片；所有漩涡产生与回收都由 LeanPool 完成。")]
    public GameObject vortexEventPrefab;
    [Header("每次漩涡风暴事件开始时创建的漩涡数量，至少1个")]
    [Min(1)] public int initialVortexCount = 2;
    [Header("屏幕内同时存在的漩涡数量上限，至少1个")]
    [Min(1)] public int maxActiveVortices = 3;
    [Header("两次随机生成漩涡的最短间隔，单位秒")]
    [Min(0.5f)] public float vortexSpawnIntervalMin = 12f;
    [Header("两次随机生成漩涡的最长间隔，单位秒")]
    [Min(0.5f)] public float vortexSpawnIntervalMax = 22f;
    [Header("单个漩涡的最短存在时间，单位秒")]
    [Min(0.5f)] public float vortexLifetimeMin = 8f;
    [Header("单个漩涡的最长存在时间，单位秒")]
    [Min(0.5f)] public float vortexLifetimeMax = 14f;
    [Header("漩涡中心位置移动速度，世界单位/秒")]
    [Min(0f)] public float vortexMoveSpeed = 0.8f;
    [Header("漩涡最短随机转向间隔，单位秒")]
    [Min(0.2f)] public float vortexTurnIntervalMin = 1.2f;
    [Header("漩涡最长随机转向间隔，单位秒")]
    [Min(0.2f)] public float vortexTurnIntervalMax = 3f;
    [Header("漩涡吸附作用范围半径，世界单位")]
    [Min(0.1f)] public float vortexAttractionRadius = 1.5f;
    [Header("靠近漩涡中心此半径后直接锁定中心，世界单位")]
    [Min(0.02f)] public float vortexCaptureRadius = 0.25f;
    [Header("漩涡把鱼拉向中心的基础速度，世界单位/秒")]
    [Min(0f)] public float vortexPullSpeed = 2.5f;
    [Header("拾取漩涡道具后将附近低等级鱼吸向中心的速度，世界单位每秒")]
    [Min(0f)] public float itemVortexPullSpeed = 4f;
    [Header("漩涡消失后，鱼继续禁止吞噬的时间，单位秒")]
    [Min(0f)] public float vortexReleaseEatDelay = 2f;
    [Header("漩涡临时占位图片的直径；使用自己的Prefab时作为参考值")]
    [Min(0.1f)] public float vortexVisualDiameter = 1f;
    [Header("漩涡事件Prefab的额外缩放倍率；用于与道具共用Prefab时增强可见性")]
    [Min(0.1f)] public float vortexEventScaleMultiplier = 1.5f;
    [Header("兼容旧配置：事件漩涡按全屏范围生成，不再使用中心区域比例")]
    [Tooltip("保留此字段以兼容旧场景和JSON配置；漩涡风暴现在使用完整相机活动范围，此值不再影响生成位置。")]
    [Range(0.1f, 1f)] public float vortexSpawnCenterRatio = 0.65f;
    [Header("漩涡与其他漩涡的最小生成距离，世界单位")]
    [Min(0f)] public float vortexMinSpacing = 1.4f;
    [Header("单次寻找漩涡出生位置的随机尝试次数")]
    [Min(1)] public int vortexPlacementAttempts = 50;
    [Header("漩涡是否吸附玩家鱼")]
    public bool vortexAffectsPlayers = true;
    [Header("漩涡是否吸附普通NPC鱼")]
    public bool vortexAffectsNpc = true;
    [Header("漩涡是否吸附特殊鱼（含鲨鱼）")]
    public bool vortexAffectsSpecialFish = true;

    [Header("鱼被吞噬时播放的特效预制体")]
    public GameObject BeEatenEff;

    [Header("玩家鱼等级提升时生成的升级特效预制体")]
    public GameObject UpgradeEff;
    [Header("极光祝福事件使用的视觉特效Prefab（可选）")]
    public GameObject auroraEventPrefab;
    [Header("疯狂海域事件可选的全屏黑暗Sprite预制体")]
    public GameObject darknessOverlayPrefab;

    [Header("两次随机事件之间的最短等待时间，秒")]
    [Min(1f)] public float specialEventIntervalMin = 30f;
    [Header("两次事件之间的最长等待时间，秒；只有等待时间随机，事件按顺序轮换")]
    [Min(1f)] public float specialEventIntervalMax = 60f;
    [Header("事件轮换从哪种开始：1疯狂海域、2极光祝福、3漩涡风暴")]
    [Range(1, 3)] public int firstSpecialEventType = 1;
    [Header("疯狂海域事件持续时间，秒")]
    [Min(1f)] public float madnessEventDuration = 15f;
    [Header("疯狂海域期间非玩家鱼移动和游泳动画速度倍率；默认2.2倍")]
    [Min(1f)] public float madnessSpeedMultiplier = 2.2f;
    [Header("疯狂海域期间NPC刷新检查加速倍率；默认3倍，更容易看到补鱼")]
    [Min(1f)] public float madnessRefreshMultiplier = 3f;
    [Header("疯狂海域期间额外允许存在的普通NPC数量；事件结束后恢复平时数量")]
    [Min(0)] public int madnessNpcExtraCount = 8;
    [Header("疯狂海域每次刷新检查最多补充多少条NPC；与刷新倍率共同生效")]
    [Min(1)] public int madnessNpcSpawnPerTick = 3;
    [Header("疯狂海域黑暗的间歇时长，秒")]
    [Min(0.1f)] public float darknessFlashDuration = 2f;
    [Header("疯狂海域随机黑暗之间的最短间隔，秒")]
    [Min(0.1f)] public float darknessFlashIntervalMin = 3f;
    [Header("疯狂海域随机黑暗之间的最长间隔，秒")]
    [Min(0.1f)] public float darknessFlashIntervalMax = 6f;
    [Header("疯狂海域黑暗叠加层的不透明度")]
    [Range(0f, 1f)] public float darknessOpacity = 0.72f;
    [Header("极光祝福事件持续时间，秒")]
    [Min(1f)] public float auroraEventDuration = 15f;
    [Header("极光祝福期间本次吞噬获得的积分倍率")]
    [Min(1f)] public float auroraScoreMultiplier = 1.5f;
    [Header("漩涡风暴随机事件持续时间，秒")]
    [Min(1f)] public float vortexStormDuration = 18f;
    [Header("升级特效在鱼身上持续显示的时间，单位秒")]
    [Min(0.1f)] public float upgradeEffectDuration = 3f;
    [Header("升级特效相对玩家鱼根节点的局部位置偏移")]
    public Vector3 upgradeEffectLocalOffset = Vector3.zero;
    [Header("是否让升级特效自动使用玩家鱼的渲染层级并显示在鱼前面")]
    public bool upgradeEffectInFront = true;
    [Header("F5运行时参数面板缩放倍率；可以在JSON中设置")]
    [Range(0.5f, 3f)] public float debugPanelScale = 1f;

    [HideInInspector] public Vector2 minBounds;
    [HideInInspector] public Vector2 maxBounds;

    private Dictionary<long, FishCore> remotePlayerFish = new Dictionary<long, FishCore>();
    private FishCore sharkFish;
    private FishCore localKeyboardFish;
    private bool localKeyboardRespawnPending;

    private bool initialNpcSeeded;
    private bool initialItemsSeeded;
    private float nextNpcPopulationCheck;
    private float nextItemSpawnTime;
    private float nextVortexSpawnTime;
    private float nextEventTime;
    private float activeEventEndTime;
    private float nextDarknessTime;
    private float darknessEndTime;
    private int activeEventType;
    private int nextSpecialEventType = 1;
    private bool hasStartedSpecialEvent;
    private GameObject auroraEffectInstance;
    private GameObject darknessEffectInstance;
    private SpriteRenderer darknessRenderer;
    private Texture2D generatedOverlayTexture;
    private Sprite generatedOverlaySprite;
    private GameObject generatedOverlayPrefab;
    private GameObject fallbackVortexPrefab;
    private Texture2D generatedVortexTexture;
    private Sprite generatedVortexSprite;
    private readonly List<ItemVortexState> activeItemVortices = new List<ItemVortexState>();
    private readonly List<SlowFieldState> activeSlowFields = new List<SlowFieldState>();
    private readonly List<FishCore> activeNpcFish = new List<FishCore>();
    private readonly List<FishCore> activeSpecialFish = new List<FishCore>();
    private readonly List<FishCore> allActiveFish = new List<FishCore>();
    private readonly List<FishVisuals> activeFishVisuals = new List<FishVisuals>();
    private readonly List<GameObject> pendingUpgradeEffectDespawn = new List<GameObject>();
    private readonly List<GameObject> pendingEquippedVortexDespawn = new List<GameObject>();
    private int nextFishSortingOrder = 1;
    private readonly List<GameObject> activeItems = new List<GameObject>();
    private readonly List<VortexState> vortices = new List<VortexState>();
    private readonly HashSet<long> pendingRemoteRespawns = new HashSet<long>();
    private int nextVortexId = 1;

    private sealed class SlowFieldState
    {
        public FishCore owner;
        public float radius;
        public float multiplier;
        public float expiresAt;
    }

    private sealed class ItemVortexState
    {
        public GameObject visual;
        public FishCore owner;
        public int ownerLevel;
        public float radius;
        public float expiresAt;
    }

    // 事件类型：0无、1疯狂海域、2极光祝福、3漩涡风暴
    public float NonPlayerEventSpeedMultiplier
    {
        get { return activeEventType == 1 && Time.time < activeEventEndTime ? Mathf.Max(1f, madnessSpeedMultiplier) : 1f; }
    }

    public float NpcScoreMultiplier
    {
        get { return activeEventType == 2 && Time.time < activeEventEndTime ? Mathf.Max(1f, auroraScoreMultiplier) : 1f; }
    }

    private sealed class VortexState
    {
        public int id;
        public GameObject visual;
        public Vector2 position;
        public Vector2 direction;
        public float expiresAt;
        public float nextTurnTime;
    }

    private void OnValidate()
    {
        NormalizeLegacyItemSlots();
    }

    private void NormalizeLegacyItemSlots()
    {
        if (Items == null) { Items = new GameObject[5]; return; }
        if (Items.Length == 5) return;

        // 从V7升级：原来的4个顺序不变，第5槽追加漩涡道具，避免旧Prefab被错误重排。
        GameObject[] corrected = new GameObject[5];
        for (int i = 0; i < Mathf.Min(4, Items.Length); i++) corrected[i] = Items[i];
        Items = corrected;
    }

    private void Awake()
    {
        NormalizeLegacyItemSlots();
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (entityRoot == null)
        {
            GameObject rootObj = new GameObject("EntityRoot");
            rootObj.transform.position = Vector3.zero;
            entityRoot = rootObj.transform;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        FishManagerRuntimeConfig runtimeConfig = GetComponent<FishManagerRuntimeConfig>();
        if (runtimeConfig == null) runtimeConfig = gameObject.AddComponent<FishManagerRuntimeConfig>();
        runtimeConfig.Initialize(this);
        CalculateScreenBounds();
    }

    private void Start()
    {
        nextSpecialEventType = Mathf.Clamp(firstSpecialEventType, 1, 3);
        SpawnInitialEntities();
        nextNpcPopulationCheck = Time.time + Mathf.Max(0.2f, npcPopulationCheckInterval);
        nextItemSpawnTime = Time.time + Mathf.Max(0.2f, itemRespawnInterval);
        ScheduleNextSpecialEvent();
    }

    private void Update()
    {
        if (Input.GetKeyDown(localPlayerSpawnKey))
            SpawnLocalKeyboardFish();

        TickSpecialEvents();

        if (Time.time >= nextNpcPopulationCheck)
        {
            float refreshMultiplier = activeEventType == 1 ? Mathf.Max(1f, madnessRefreshMultiplier) : 1f;
            nextNpcPopulationCheck = Time.time + Mathf.Max(0.2f, npcPopulationCheckInterval / refreshMultiplier);
            RefreshNpcPopulation(false);
            RefreshSpecialFishPopulation();
        }

        if (Time.time >= nextItemSpawnTime)
        {
            nextItemSpawnTime = Time.time + Mathf.Max(0.2f, itemRespawnInterval);
            if (activeEventType != 2) RefreshItemPopulation(false);
        }

        if (activeEventType == 3 && Time.time >= nextVortexSpawnTime)
        {
            if (vortices.Count < Mathf.Max(1, maxActiveVortices)) SpawnVortexEvent();
            ScheduleNextVortex();
        }
    }

    private void FixedUpdate()
    {
        TickVortexEvents();
        TickItemVortices();
        TickSlowFields();
    }

    private void LateUpdate()
    {
        // 不在 FishCore.OnDisable() 的父物体禁用回调中调用 LeanPool.Despawn。
        // 等整棵层级的启用/禁用过程结束后，统一安全回收到 LeanPool。
        for (int i = 0; i < pendingUpgradeEffectDespawn.Count; i++)
        {
            GameObject effect = pendingUpgradeEffectDespawn[i];
            if (effect != null) LeanPool.Despawn(effect);
        }
        pendingUpgradeEffectDespawn.Clear();

        // 若外部脚本突然禁用了玩家鱼，在 OnDisable 后再回收其道具漩涡，
        // 避免 LeanPool 在父物体启停过程中调用 Transform.SetParent 报错。
        for (int i = 0; i < pendingEquippedVortexDespawn.Count; i++)
        {
            GameObject itemObj = pendingEquippedVortexDespawn[i];
            if (itemObj != null) ReturnPooledEventVisual(itemObj);
        }
        pendingEquippedVortexDespawn.Clear();

        if (mainCamera == null) return;
        if (lastCameraWidth != mainCamera.pixelWidth || lastCameraHeight != mainCamera.pixelHeight ||
            !Mathf.Approximately(lastCameraAspect, mainCamera.aspect) ||
            !Mathf.Approximately(lastCameraFov, mainCamera.fieldOfView) ||
            !Mathf.Approximately(lastCameraSize, mainCamera.orthographicSize) ||
            !Mathf.Approximately(lastFishPlaneZ, ActiveFishPlaneZ) ||
            !Mathf.Approximately(lastBottomBoundaryOffset, bottomBoundaryOffset) ||
            !Mathf.Approximately(lastTopBoundaryOffset, topBoundaryOffset) ||
            !Mathf.Approximately(lastScreenEdgePadding, screenEdgePadding) ||
            lastCameraPosition != mainCamera.transform.position || lastCameraRotation != mainCamera.transform.rotation)
            CalculateScreenBounds();
    }

    private void CalculateScreenBounds()
    {
        if (mainCamera == null) return;

        Plane fishPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, ActiveFishPlaneZ));
        Vector3 bottomLeft, bottomRight, topLeft, topRight;
        if (!TryGetWorldPoint(0f, 0f, fishPlane, out bottomLeft) ||
            !TryGetWorldPoint(1f, 0f, fishPlane, out bottomRight) ||
            !TryGetWorldPoint(0f, 1f, fishPlane, out topLeft) ||
            !TryGetWorldPoint(1f, 1f, fishPlane, out topRight))
        {
            Debug.LogWarning("相机没有朝向鱼所在的 Z 平面，请检查 Camera 和 fishPlaneZ！");
            return;
        }

        float minX = Mathf.Min(Mathf.Min(bottomLeft.x, bottomRight.x), Mathf.Min(topLeft.x, topRight.x));
        float maxX = Mathf.Max(Mathf.Max(bottomLeft.x, bottomRight.x), Mathf.Max(topLeft.x, topRight.x));
        float minY = Mathf.Min(Mathf.Min(bottomLeft.y, bottomRight.y), Mathf.Min(topLeft.y, topRight.y));
        float maxY = Mathf.Max(Mathf.Max(bottomLeft.y, bottomRight.y), Mathf.Max(topLeft.y, topRight.y));
        float padX = (maxX - minX) * screenEdgePadding;
        float padY = (maxY - minY) * screenEdgePadding;
        float adjustedMinY = minY + padY + bottomBoundaryOffset;
        float adjustedMaxY = maxY - padY - topBoundaryOffset;
        if (adjustedMinY >= adjustedMaxY)
        {
            float centerY = (minY + maxY) * 0.5f;
            adjustedMinY = centerY - 0.01f;
            adjustedMaxY = centerY + 0.01f;
        }
        minBounds = new Vector2(minX + padX, adjustedMinY);
        maxBounds = new Vector2(maxX - padX, adjustedMaxY);

        lastCameraWidth = mainCamera.pixelWidth;
        lastCameraHeight = mainCamera.pixelHeight;
        lastCameraAspect = mainCamera.aspect;
        lastCameraFov = mainCamera.fieldOfView;
        lastCameraSize = mainCamera.orthographicSize;
        lastFishPlaneZ = ActiveFishPlaneZ;
        lastBottomBoundaryOffset = bottomBoundaryOffset;
        lastTopBoundaryOffset = topBoundaryOffset;
        lastScreenEdgePadding = screenEdgePadding;
        lastCameraPosition = mainCamera.transform.position;
        lastCameraRotation = mainCamera.transform.rotation;

        Debug.Log($"<color=green>鱼活动范围：X [{minBounds.x:F2}, {maxBounds.x:F2}], Y [{minBounds.y:F2}, {maxBounds.y:F2}], 相机 {mainCamera.name}，宽高比 {mainCamera.aspect:F3}，鱼平面 Z={ActiveFishPlaneZ:F2}</color>");
    }

    private bool TryGetWorldPoint(float x, float y, Plane plane, out Vector3 point)
    {
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(x, y, 0f));
        float distance;
        if (plane.Raycast(ray, out distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    public Vector2 ClampPosition(Vector2 pos)
    {
        return new Vector2(Mathf.Clamp(pos.x, minBounds.x, maxBounds.x), Mathf.Clamp(pos.y, minBounds.y, maxBounds.y));
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(new Vector3((minBounds.x + maxBounds.x) * 0.5f, (minBounds.y + maxBounds.y) * 0.5f, ActiveFishPlaneZ),
            new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0f));

        Gizmos.color = Color.cyan;
        for (int i = 0; i < vortices.Count; i++)
        {
            if (vortices[i].visual == null) continue;
            Vector2 center = vortices[i].position;
            Gizmos.DrawWireSphere(new Vector3(center.x, center.y, ActiveFishPlaneZ),
                Mathf.Max(0.1f, vortexAttractionRadius));
        }
    }

    private void SpawnInitialEntities()
    {
        if (!initialNpcSeeded)
        {
            initialNpcSeeded = true;
            SpawnIdleNpcFish();
        }

        if (!initialItemsSeeded)
        {
            initialItemsSeeded = true;
            RefreshItemPopulation(true);
        }

        // 场景漩涡只由随机的“漩涡风暴”事件生成；特殊鱼不再开场保底生成。
        if (!HasActivePlayers()) return;
        RefreshNpcPopulation(true);
    }

    private int GetDefaultSpecialFishLevel()
    {
        // 特殊鱼闪电、黄金、巨人均按策划随机3～6级。
        return Random.Range(3, 7);
    }

    private int CountActiveSpecialFish()
    {
        for (int i = activeSpecialFish.Count - 1; i >= 0; i--)
        {
            FishCore fish = activeSpecialFish[i];
            if (fish == null || !fish.gameObject.activeInHierarchy || fish.isDead || fish.specialFishType < 0)
                activeSpecialFish.RemoveAt(i);
        }
        return activeSpecialFish.Count;
    }

    public int GetActivePlayerCount()
    {
        int count = 0;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].activeInHierarchy) continue;
            FishCore fish = players[i].GetComponent<FishCore>();
            if (fish != null && fish.isPlayer && !fish.isDead) count++;
        }
        return count;
    }

    public float GetAveragePlayerLevel()
    {
        float levels = 0f;
        int count = 0;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].activeInHierarchy) continue;
            FishCore fish = players[i].GetComponent<FishCore>();
            if (fish == null || !fish.isPlayer || fish.isDead) continue;
            levels += Mathf.Clamp(fish.level, 1, 10);
            count++;
        }
        return count > 0 ? levels / count : 1f;
    }

    public float GetSpecialFishSpawnChance()
    {
        return Mathf.Clamp01(specialFishBaseChance + GetActivePlayerCount() * specialFishChancePerPlayer);
    }

    public float GetItemSpawnChance()
    {
        return Mathf.Clamp01(itemBaseChance + GetActivePlayerCount() * itemChancePerPlayer);
    }

    private int GetDynamicMaxSpecialCount()
    {
        int baseCount = Mathf.Max(0, initialSpecialCount);
        int extra = Mathf.FloorToInt(Mathf.Max(0f, GetAveragePlayerLevel() - 1f) *
            Mathf.Max(0f, specialExtraPerAverageLevel));
        return baseCount + extra;
    }

    private int GetDynamicMaxItemCount()
    {
        int baseCount = Mathf.Max(0, initialItemCount);
        int extra = Mathf.FloorToInt(Mathf.Max(0f, GetAveragePlayerLevel() - 1f) *
            Mathf.Max(0f, itemExtraPerAverageLevel));
        return Mathf.Min(Mathf.Max(0, maxItemCount), baseCount + extra);
    }

    private void SpawnInitialSpecialFish()
    {
        // 保留原方法名便于旧工程扩展；本版本改为概率刷新，不在开场保底生成。
    }

    private void RefreshSpecialFishPopulation()
    {
        int alive = CountActiveSpecialFish();
        int limit = GetDynamicMaxSpecialCount();

        // 缩容时先回收普通特殊鱼；鲨鱼仍按照追逐结束或无人时向左右离场。
        for (int i = activeSpecialFish.Count - 1; i >= 0 && alive > limit; i--)
        {
            FishCore fish = activeSpecialFish[i];
            if (fish == null || fish.specialFishType == 3) continue;
            activeSpecialFish.RemoveAt(i);
            if (fish.gameObject.activeInHierarchy) LeanPool.Despawn(fish.gameObject);
            alive--;
        }

        if (activeEventType == 2 || alive >= limit) return;
        if (Random.value >= GetSpecialFishSpawnChance()) return;

        List<int> available = new List<int>();
        if (SpecialFish == null) return;
        int numPlayers = GetActivePlayerCount();
        for (int i = 0; i < SpecialFish.Length && i < 4; i++)
        {
            if (SpecialFish[i] == null) continue;
            if (i == 3 && (numPlayers == 0 || (sharkFish != null && sharkFish.gameObject.activeInHierarchy))) continue;
            available.Add(i);
        }

        if (available.Count == 0) return;

        int specialType = available[Random.Range(0, available.Count)];
        int level = specialType == 3 ? 9 : GetDefaultSpecialFishLevel();
        SpawnFish(SpecialFish[specialType], GetRandomPosition(), level, false);
    }

    private void AssignFishSortingOrder(FishVisuals fishVisuals)
    {
        if (fishVisuals == null || fishVisuals.spriteRenderer == null) return;

        // 同一个LeanPool实例每次重新生成，也要获得最新的排序序号。
        activeFishVisuals.Remove(fishVisuals);
        if (nextFishSortingOrder > 32000) RebuildActiveFishSortingOrders();

        int order = nextFishSortingOrder++;
        fishVisuals.SetSortingOrder(order);
        activeFishVisuals.Add(fishVisuals);
    }

    private void RebuildActiveFishSortingOrders()
    {
        for (int i = activeFishVisuals.Count - 1; i >= 0; i--)
        {
            FishVisuals fish = activeFishVisuals[i];
            if (fish == null || !fish.gameObject.activeInHierarchy)
                activeFishVisuals.RemoveAt(i);
        }

        nextFishSortingOrder = 1;
        for (int i = 0; i < activeFishVisuals.Count; i++)
        {
            if (nextFishSortingOrder > 32766)
            {
                Debug.LogError("场上鱼数量超过排序支持上限，无法保证全部SpriteRenderer使用唯一Order in Layer。");
                nextFishSortingOrder = 32767;
                break;
            }

            activeFishVisuals[i].SetSortingOrder(nextFishSortingOrder++);
        }
    }

    private void SpawnIdleNpcFish()
    {
        if (NPCFish == null || NPCFish.Length == 0 || initialNpcCount <= 0) return;

        List<int> available = new List<int>();
        List<GameObject> uniquePrefabs = new List<GameObject>();
        for (int i = 0; i < NPCFish.Length; i++)
        {
            if (NPCFish[i] == null || uniquePrefabs.Contains(NPCFish[i])) continue;
            available.Add(i);
            uniquePrefabs.Add(NPCFish[i]);
        }

        int targetCount = initialNpcCount / 2;
        if (keepEveryNpcTypeVisible) targetCount = Mathf.Max(targetCount, available.Count);
        if (targetCount <= 0 || available.Count == 0) return;

        // 打乱先后顺序，优先确保每个非空 NPC 预制体都至少创建一次。
        for (int i = available.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int temp = available[i];
            available[i] = available[j];
            available[j] = temp;
        }

        int guaranteed = keepEveryNpcTypeVisible ? Mathf.Min(targetCount, available.Count) : 0;
        for (int i = 0; i < guaranteed; i++)
        {
            int prefabIndex = available[i];
            SpawnFish(NPCFish[prefabIndex], GetRandomPosition(), Mathf.Clamp(prefabIndex + 1, 1, 7), false);
        }

        for (int i = guaranteed; i < targetCount; i++)
        {
            int prefabIndex = available[Random.Range(0, available.Count)];
            SpawnFish(NPCFish[prefabIndex], GetRandomPosition(), Mathf.Clamp(prefabIndex + 1, 1, 7), false);
        }
    }

    private int GetDesiredNpcCount()
    {
        if (initialNpcCount <= 0) return 0;
        int target = initialNpcCount;
        if (!HasActivePlayers())
        {
            target = initialNpcCount / 2;
            if (keepEveryNpcTypeVisible && NPCFish != null)
            {
                List<GameObject> uniquePrefabs = new List<GameObject>();
                for (int i = 0; i < NPCFish.Length; i++)
                    if (NPCFish[i] != null && !uniquePrefabs.Contains(NPCFish[i])) uniquePrefabs.Add(NPCFish[i]);
                target = Mathf.Max(target, uniquePrefabs.Count);
            }
        }
        if (activeEventType == 1 && Time.time < activeEventEndTime)
            target += Mathf.Max(0, madnessNpcExtraCount);
        return target;
    }

    private void RefreshNpcPopulation(bool fillAll)
    {
        for (int i = activeNpcFish.Count - 1; i >= 0; i--)
        {
            FishCore fish = activeNpcFish[i];
            if (fish == null || !fish.gameObject.activeInHierarchy || fish.isDead || fish.isPlayer || fish.specialFishType >= 0)
                activeNpcFish.RemoveAt(i);
        }

        int targetCount = GetDesiredNpcCount();

        // 玩家全部离场后，普通 NPC 缩减回无人状态的数量，不播放被吃特效。
        while (activeNpcFish.Count > targetCount)
        {
            FishCore fish = activeNpcFish[activeNpcFish.Count - 1];
            activeNpcFish.RemoveAt(activeNpcFish.Count - 1);
            if (fish != null && fish.gameObject.activeInHierarchy) LeanPool.Despawn(fish.gameObject);
        }

        if (!HasActivePlayers() && keepEveryNpcTypeVisible && NPCFish != null && initialNpcCount > 0)
        {
            for (int slot = 0; slot < NPCFish.Length; slot++)
            {
                if (NPCFish[slot] == null) continue;
                bool duplicatedSlot = false;
                for (int earlier = 0; earlier < slot; earlier++)
                    if (NPCFish[earlier] == NPCFish[slot]) { duplicatedSlot = true; break; }
                if (duplicatedSlot) continue;

                bool exists = false;
                for (int i = 0; i < activeNpcFish.Count; i++)
                    if (activeNpcFish[i].npcPrefabSlot == slot) { exists = true; break; }

                if (exists) continue;

                if (activeNpcFish.Count >= targetCount)
                {
                    int removeIndex = -1;
                    for (int i = activeNpcFish.Count - 1; i >= 0; i--)
                    {
                        int indexToCheck = activeNpcFish[i].npcPrefabSlot;
                        int copies = 0;
                        for (int j = 0; j < activeNpcFish.Count; j++)
                            if (activeNpcFish[j].npcPrefabSlot == indexToCheck) copies++;
                        if (copies > 1 || indexToCheck < 0) { removeIndex = i; break; }
                    }

                    if (removeIndex < 0) break;
                    FishCore duplicate = activeNpcFish[removeIndex];
                    activeNpcFish.RemoveAt(removeIndex);
                    if (duplicate != null && duplicate.gameObject.activeInHierarchy) LeanPool.Despawn(duplicate.gameObject);
                }

                SpawnFish(NPCFish[slot], GetRandomPosition(), Mathf.Clamp(slot + 1, 1, 7), false);
            }
        }

        int maximumPerCheck = activeEventType == 1 && Time.time < activeEventEndTime
            ? Mathf.Max(1, madnessNpcSpawnPerTick) : 1;
        int remaining = fillAll ? targetCount - activeNpcFish.Count :
            Mathf.Min(maximumPerCheck, targetCount - activeNpcFish.Count);
        for (int i = 0; i < remaining; i++)
        {
            if (NPCFish == null || NPCFish.Length == 0) break;
            int level = GetNpcLevelByPlayerDistribution();
            GameObject prefab = GetNpcPrefabForLevel(level);
            if (prefab == null) break;
            if (SpawnFish(prefab, GetRandomPosition(), level, false) == null) break;
        }
    }

    public void NotifyFishDespawned(FishCore fish)
    {
        if (fish == null) return;
        ReleaseEquippedVorticesForOwner(fish);
        activeNpcFish.Remove(fish);
        activeSpecialFish.Remove(fish);
        allActiveFish.Remove(fish);
        FishVisuals visuals = fish.GetComponent<FishVisuals>();
        if (visuals != null) activeFishVisuals.Remove(visuals);
    }

    private void RefreshItemPopulation(bool fillAll)
    {
        NormalizeLegacyItemSlots();

        for (int i = activeItems.Count - 1; i >= 0; i--)
            if (activeItems[i] == null || !activeItems[i].activeInHierarchy) activeItems.RemoveAt(i);

        int allowedCount = GetDynamicMaxItemCount();
        while (activeItems.Count > allowedCount)
        {
            GameObject item = activeItems[activeItems.Count - 1];
            activeItems.RemoveAt(activeItems.Count - 1);
            if (item != null && item.activeInHierarchy) LeanPool.Despawn(item);
        }

        int targetCount = fillAll ? Mathf.Min(Mathf.Max(0, initialItemCount), allowedCount) : allowedCount;
        int missing = targetCount - activeItems.Count;
        if (missing <= 0 || Items == null) return;

        // 游戏启动时仍然按初始数量出现一些道具；之后每次只做一次概率判定。
        if (!fillAll && Random.value >= GetItemSpawnChance()) return;

        List<int> availableTypes = new List<int>();
        for (int i = 0; i < Items.Length; i++)
        {
            if (Items[i] == null) continue;
            ItemType type = GetItemTypeByIndex(i);
            bool exists = false;

            for (int j = 0; j < activeItems.Count; j++)
            {
                GameObject obj = activeItems[j];
                if (obj == null || !obj.activeInHierarchy) continue;
                ItemBase item = obj.GetComponent<ItemBase>();
                if (item != null && item.type == type) { exists = true; break; }
            }

            if (!exists) availableTypes.Add(i);
        }

        if (availableTypes.Count == 0) return;

        // 不允许同种道具在场重复；初始尽量凑齐不同的种类。
        int spawnCount = fillAll ? Mathf.Min(missing, availableTypes.Count) : 1;
        for (int i = 0; i < spawnCount && availableTypes.Count > 0; i++)
        {
            Vector2 spawnPosition;
            if (!TryFindItemSpawnPosition(out spawnPosition)) break;
            int choice = Random.Range(0, availableTypes.Count);
            int slot = availableTypes[choice];
            availableTypes.RemoveAt(choice);
            SpawnItem(slot, spawnPosition);
        }
    }

    public void NotifyItemDespawned(GameObject item)
    {
        if (item != null) activeItems.Remove(item);
    }

    private void SpawnLocalKeyboardFish()
    {
        if (localKeyboardRespawnPending)
        {
            Debug.Log("本地键盘鱼正在等待复活；请稍候。");
            return;
        }

        if (localKeyboardFish != null && localKeyboardFish.gameObject.activeInHierarchy)
        {
            Debug.Log("本地键盘鱼已存在；无需重复创建。");
            return;
        }

        if (PlayerFish == null || PlayerFish.Length == 0 || PlayerFish[0] == null)
        {
            Debug.LogWarning("无法创建本地键盘鱼：请在 FishManager.PlayerFish[0] 配置玩家鱼预制体。");
            return;
        }

        localKeyboardFish = SpawnFish(
            PlayerFish[0],
            (minBounds + maxBounds) * 0.5f,
            1,
            true,
            0L,
            "本地键盘玩家");
        if (localKeyboardFish != null)
        {
            localKeyboardFish.SetBoostMultiplier(localBoostMultiplier);
            SpawnInitialEntities();
            Debug.Log("已创建本地键盘鱼：方向键/WASD 移动，空格加速。");
        }
    }

    private int GetNpcLevelByPlayerDistribution()
    {
        float levelSum = 0f;
        int playerCount = 0;
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].activeInHierarchy) continue;
            FishCore player = players[i].GetComponent<FishCore>();
            if (player == null || !player.isPlayer || player.isDead) continue;
            levelSum += player.level;
            playerCount++;
        }

        // 尚无玩家时用 4 级作为初始参考等级；有玩家时按在场玩家平均等级调整 NPC 等级区间。
        float referenceLevel = playerCount > 0 ? levelSum / playerCount : 4f;
        int maxNpcLevel = Mathf.Max(1, Mathf.Min(7, NPCFish.Length));
        float roll = Random.value;
        int minLevel;
        int maxLevel;
        if (roll < 0.6f) // 低等级 60%
        {
            minLevel = Mathf.Max(1, Mathf.FloorToInt(referenceLevel) - 3);
            maxLevel = Mathf.Max(minLevel, Mathf.FloorToInt(referenceLevel) - 1);
        }
        else if (roll < 0.9f) // 中等级 30%
        {
            minLevel = Mathf.Max(1, Mathf.RoundToInt(referenceLevel) - 1);
            maxLevel = Mathf.Max(minLevel, Mathf.RoundToInt(referenceLevel) + 1);
        }
        else // 高等级 10%
        {
            minLevel = Mathf.Max(1, Mathf.CeilToInt(referenceLevel) + 1);
            maxLevel = Mathf.Max(minLevel, Mathf.CeilToInt(referenceLevel) + 3);
        }

        minLevel = Mathf.Clamp(minLevel, 1, maxNpcLevel);
        maxLevel = Mathf.Clamp(maxLevel, minLevel, maxNpcLevel);
        return Random.Range(minLevel, maxLevel + 1);
    }

    private GameObject GetNpcPrefabForLevel(int level)
    {
        if (NPCFish == null || NPCFish.Length == 0) return null;
        int preferredIndex = Mathf.Clamp(level - 1, 0, NPCFish.Length - 1);
        if (NPCFish[preferredIndex] != null) return NPCFish[preferredIndex];

        for (int i = 0; i < NPCFish.Length; i++)
            if (NPCFish[i] != null) return NPCFish[i];
        return null;
    }

    private Vector2 GetRandomPosition()
    {
        return new Vector2(Random.Range(minBounds.x, maxBounds.x), Random.Range(minBounds.y, maxBounds.y));
    }

    public FishCore SpawnFish(GameObject prefab, Vector2 pos, int level, bool isPlayer, long playerId = 0L, string name = "NPC", bool grantSpawnInvincibility = true)
    {
        if (prefab == null) return null;
        GameObject obj = LeanPool.Spawn(prefab, entityRoot);
        pos = ClampPosition(pos);
        obj.transform.position = new Vector3(pos.x, pos.y, ActiveFishPlaneZ);

        FishCore core = obj.GetComponent<FishCore>();
        if (core == null)
        {
            Debug.LogError("鱼预制体缺少 FishCore：" + prefab.name);
            LeanPool.Despawn(obj);
            return null;
        }

        core.level = level;
        core.isPlayer = isPlayer;
        core.playerName = name;
        core.playerId = playerId;
        core.specialFishType = -1;
        core.npcPrefabSlot = -1;
        for (int i = 0; SpecialFish != null && i < SpecialFish.Length; i++)
        {
            if (SpecialFish[i] == prefab)
            {
                core.specialFishType = i;
                break;
            }
        }
        if (!isPlayer && core.specialFishType < 0 && NPCFish != null)
        {
            for (int i = 0; i < NPCFish.Length; i++)
            {
                if (NPCFish[i] == prefab)
                {
                    core.npcPrefabSlot = i;
                    break;
                }
            }
        }

        core.ResetGameplayEffects();
        core.isDead = false;
        core.score = 0;
        core.currentGrowth = 0;
        core.isInvincible = false;
        core.isInvisible = false;
        core.canEatHigher = false;
        core.SetBoosting(false);
        core.SetBoostMultiplier(localBoostMultiplier);
        SpriteRenderer[] fishRenderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < fishRenderers.Length; i++)
        {
            Color color = fishRenderers[i].color;
            color.a = 1f;
            fishRenderers[i].color = color;
        }
        core.speedMultiplier = core.specialFishType == 0 ? 1.5f :
            (core.specialFishType == 3 ? Mathf.Max(1f, sharkSpeedMultiplier) : 1f);
        core.baseSpeed = globalBaseSpeed;
        core.RefreshLevelSpeed();

        if (isPlayer)
        {
            obj.tag = "Player";
            PlayerInputMovement inputComp = obj.GetComponent<PlayerInputMovement>();
            if (inputComp != null)
            {
                if (playerId > 0)
                {
                    inputComp.SetRemoteMode(true);
                    remotePlayerFish[playerId] = core;
                }
                else inputComp.SetRemoteMode(false);
            }
        }
        else obj.tag = "Untagged";

        core.UpdateVisuals();
        allActiveFish.Remove(core);
        allActiveFish.Add(core);
        AssignFishSortingOrder(obj.GetComponent<FishVisuals>());

        if (!isPlayer && core.specialFishType >= 0 && core.specialFishType < 4)
        {
            activeSpecialFish.Remove(core);
            activeSpecialFish.Add(core);
        }

        if (!isPlayer && core.specialFishType < 0)
        {
            activeNpcFish.Remove(core);
            activeNpcFish.Add(core);
        }
        if (isPlayer)
        {
            if (grantSpawnInvincibility)
                core.ApplyInvincibility(playerSpawnInvincibleSeconds);
            if (playerId == 0L) localKeyboardFish = core;
        }
        if (core.specialFishType == 3) sharkFish = core;
        if (core.specialFishType == 2) core.StartSpecialLifetime(10f);
        return core;
    }


    public bool HasActivePlayers()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].activeInHierarchy) continue;
            FishCore core = players[i].GetComponent<FishCore>();
            if (core != null && core.isPlayer && !core.isDead) return true;
        }
        return false;
    }

    private void TrySpawnSharkIfNeeded()
    {
        // 不再由玩家入场必定生成，鲨鱼仅在随机刷新命中概率并选中鲨鱼时出现。
        if (!HasActivePlayers() || SpecialFish == null || SpecialFish.Length <= 3 || SpecialFish[3] == null) return;
        if (sharkFish != null && sharkFish.gameObject.activeInHierarchy) return;
        if (CountActiveSpecialFish() >= GetDynamicMaxSpecialCount()) return;
        SpawnFish(SpecialFish[3], GetRandomPosition(), 9, false);
    }

    public void NotifySharkDespawned(FishCore shark)
    {
        if (sharkFish == shark) sharkFish = null;
    }

    public void ApplySlowTime(FishCore source, float multiplier, float duration)
    {
        ApplySlowTime(source, multiplier, duration, 3f);
    }

    public void ApplySlowTime(FishCore source, float multiplier, float duration, float radius)
    {
        if (source == null || duration <= 0f) return;

        // 五秒内范围始终随持有者移动，后续靠近的鱼也会被减速。
        SlowFieldState field = new SlowFieldState();
        field.owner = source;
        field.radius = Mathf.Max(0.1f, radius);
        field.multiplier = Mathf.Clamp01(multiplier);
        field.expiresAt = Time.time + duration;
        activeSlowFields.Add(field);
        TickSlowFields();
    }

    private void TickSlowFields()
    {
        for (int i = activeSlowFields.Count - 1; i >= 0; i--)
        {
            SlowFieldState field = activeSlowFields[i];
            if (field.owner == null || field.owner.isDead ||
                !field.owner.gameObject.activeInHierarchy || Time.time >= field.expiresAt)
            {
                activeSlowFields.RemoveAt(i);
                continue;
            }

            float radiusSquared = field.radius * field.radius;
            Vector2 center = field.owner.transform.position;
            for (int j = 0; j < allActiveFish.Count; j++)
            {
                FishCore target = allActiveFish[j];
                if (target == null || target == field.owner || target.isDead || !target.gameObject.activeInHierarchy)
                    continue;
                if (((Vector2)target.transform.position - center).sqrMagnitude > radiusSquared) continue;
                target.ApplyAreaSlow(field.multiplier, Time.fixedDeltaTime * 2f + 0.02f);
            }
        }
    }

    private float GetItemVortexRadius(FishCore owner, float fallbackRadius)
    {
        if (owner != null)
        {
            FishVisuals fishVisuals = owner.GetComponent<FishVisuals>();
            SpriteRenderer fishSprite = fishVisuals != null ? fishVisuals.spriteRenderer : null;
            if (fishSprite != null)
            {
                Vector3 fishSize = fishSprite.bounds.size;
                float diameter = Mathf.Max(fishSize.x, fishSize.y);
                if (diameter > 0.001f) return diameter * 1.5f;
            }
        }

        return Mathf.Max(0.1f, fallbackRadius);
    }

    public bool EquipVortexItem(FishCore owner, ItemBase item, float duration, float fallbackRadius)
    {
        if (owner == null || !owner.isPlayer || owner.isDead || item == null ||
            item.type != ItemType.Vortex || item.IsEquippedVortex ||
            !item.gameObject.activeInHierarchy) return false;

        // 关键：使用触发拾取的这一个道具实例，绝对不 Instantiate，也不 LeanPool.Spawn 新漩涡。
        if (!item.MarkAsEquippedVortex()) return false;
        NotifyItemDespawned(item.gameObject);

        Transform itemTransform = item.transform;
        itemTransform.SetParent(owner.transform, false);
        itemTransform.localPosition = Vector3.zero;
        itemTransform.localRotation = Quaternion.identity;
        itemTransform.localScale = item.equippedVortexLocalScale;

        // 道具变成鱼的子物体后，正常绘制在鱼的上层，避免被鱼身完全遮住。
        FishVisuals visuals = owner.GetComponent<FishVisuals>();
        SpriteRenderer fishRenderer = visuals != null ? visuals.spriteRenderer : null;
        if (fishRenderer != null)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sortingLayerID = fishRenderer.sortingLayerID;
                renderers[i].sortingOrder = Mathf.Min(32767, fishRenderer.sortingOrder + 1);
            }
        }
        RestartPooledParticles(item.gameObject);

        ItemVortexState state = new ItemVortexState();
        state.visual = item.gameObject;
        state.owner = owner;
        state.ownerLevel = owner.level;
        state.radius = GetItemVortexRadius(owner, fallbackRadius);
        state.expiresAt = Time.time + Mathf.Max(0.1f, duration);
        activeItemVortices.Add(state);
        return true;
    }

    private void ReturnEquippedVortex(ItemVortexState state)
    {
        if (state == null || state.visual == null) return;
        GameObject itemObj = state.visual;
        state.visual = null;
        ReturnPooledEventVisual(itemObj);
    }

    public void ReleaseEquippedVorticesForOwner(FishCore owner)
    {
        if (owner == null) return;

        // 在父鱼调用 LeanPool.Despawn 前回收其所有道具，防止 SetParent 在 OnDisable 内发生。
        for (int i = activeItemVortices.Count - 1; i >= 0; i--)
        {
            if (activeItemVortices[i].owner != owner) continue;
            ItemVortexState state = activeItemVortices[i];
            activeItemVortices.RemoveAt(i);
            ReturnEquippedVortex(state);
        }
    }

    public void TransferEquippedVorticesToUpgradedFish(FishCore oldFish, FishCore newFish)
    {
        if (oldFish == null || newFish == null) return;

        // 升级换整条玩家鱼时，继续使用同一个道具实例和原定时长。
        for (int i = 0; i < activeItemVortices.Count; i++)
        {
            ItemVortexState state = activeItemVortices[i];
            if (state.owner != oldFish || state.visual == null) continue;

            ItemBase item = state.visual.GetComponent<ItemBase>();
            state.visual.transform.SetParent(newFish.transform, false);
            state.visual.transform.localPosition = Vector3.zero;
            state.visual.transform.localRotation = Quaternion.identity;
            if (item != null) state.visual.transform.localScale = item.equippedVortexLocalScale;

            FishVisuals newVisuals = newFish.GetComponent<FishVisuals>();
            SpriteRenderer newFishSprite = newVisuals != null ? newVisuals.spriteRenderer : null;
            if (newFishSprite != null)
            {
                Renderer[] renderers = state.visual.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    renderers[rendererIndex].sortingLayerID = newFishSprite.sortingLayerID;
                    renderers[rendererIndex].sortingOrder = Mathf.Min(32767, newFishSprite.sortingOrder + 1);
                }
            }

            state.owner = newFish;
            state.ownerLevel = newFish.level;
            state.radius = GetItemVortexRadius(newFish, state.radius);
        }
    }

    public void QueueEquippedVorticesForSafeDespawn(FishCore owner)
    {
        if (owner == null) return;

        // 只移除逻辑记录，绝不在 OnDisable 中回收或修改道具父节点。
        for (int i = activeItemVortices.Count - 1; i >= 0; i--)
        {
            ItemVortexState state = activeItemVortices[i];
            if (state.owner != owner) continue;
            activeItemVortices.RemoveAt(i);
            if (state.visual != null && !pendingEquippedVortexDespawn.Contains(state.visual))
                pendingEquippedVortexDespawn.Add(state.visual);
        }
    }

    private void TickItemVortices()
    {
        if (activeItemVortices.Count == 0) return;

        for (int i = allActiveFish.Count - 1; i >= 0; i--)
        {
            FishCore fish = allActiveFish[i];
            if (fish == null || !fish.gameObject.activeInHierarchy || fish.isDead)
                allActiveFish.RemoveAt(i);
        }

        for (int i = activeItemVortices.Count - 1; i >= 0; i--)
        {
            ItemVortexState state = activeItemVortices[i];
            if (state.visual == null || state.owner == null || state.owner.isDead ||
                !state.owner.gameObject.activeInHierarchy || Time.time >= state.expiresAt)
            {
                activeItemVortices.RemoveAt(i);
                ReturnEquippedVortex(state);
                continue;
            }

            // 父子关系自动让漩涡跟随鱼移动，不通过逐帧修改漩涡世界坐标来“假跟随”。
            state.ownerLevel = state.owner.level;
            ItemBase equippedItem = state.visual.GetComponent<ItemBase>();
            float fallbackRadius = equippedItem != null ? equippedItem.vortexRadius : state.radius;
            state.radius = GetItemVortexRadius(state.owner, fallbackRadius);
            Vector2 center = state.owner.transform.position;
            float radiusSquared = state.radius * state.radius;

            for (int j = 0; j < allActiveFish.Count; j++)
            {
                FishCore fish = allActiveFish[j];
                if (fish == null || fish == state.owner || fish.isDead ||
                    !fish.gameObject.activeInHierarchy) continue;

                // 只吸附持有者当前规则下真正能吃的鱼，而不是简单比较等级：
                // 1) NPC/特殊鱼：玩家等级 >= 目标等级即可吸附；
                // 2) 其他玩家鱼：必须持有猎食令、等级高于目标，且目标不处于无敌状态；
                // 3) 漩涡禁食保护生效时，暂不吸附，避免吸到却无法吞噬。
                if (state.owner.IsVortexEatingBlocked() || fish.IsVortexEatingBlocked()) continue;

                bool canOwnerEat;
                if (fish.isPlayer)
                {
                    canOwnerEat = state.owner.canEatHigher &&
                                  state.owner.level > fish.level &&
                                  !fish.isInvincible;
                }
                else
                {
                    canOwnerEat = state.owner.level >= fish.level;
                }

                if (!canOwnerEat) continue;
                if (((Vector2)fish.transform.position - center).sqrMagnitude > radiusSquared) continue;

                // 道具漩涡不施加漩涡风暴的禁食保护；吸入的目标仍按普通吃鱼规则处理。
                fish.ApplyItemVortexPull(state.visual.transform, Mathf.Max(0.1f, state.expiresAt - Time.time));
            }
        }
    }

    public void HandlePlayerDeath(FishCore player)
    {
        if (player == null) return;
        Vector3 spawnPosition = player.transform.position;
        long playerId = player.playerId;
        string playerName = player.playerName;
        if (playerId > 0)
        {
            remotePlayerFish.Remove(playerId);
            pendingRemoteRespawns.Add(playerId);
        }
        else
        {
            if (localKeyboardFish == player) localKeyboardFish = null;
            localKeyboardRespawnPending = true;
        }
        player.PrepareUpgradeEffectForDespawn();
        LeanPool.Despawn(player.gameObject);
        StartCoroutine(RespawnPlayerAfterDelay(playerId, playerName, spawnPosition));
    }

    private System.Collections.IEnumerator RespawnPlayerAfterDelay(long playerId, string playerName, Vector3 spawnPosition)
    {
        yield return new WaitForSeconds(3f);
        if (playerId > 0 && !pendingRemoteRespawns.Remove(playerId)) yield break;
        if (playerId == 0L && !localKeyboardRespawnPending) yield break;
        if (PlayerFish == null || PlayerFish.Length == 0 || PlayerFish[0] == null)
        {
            if (playerId == 0L) localKeyboardRespawnPending = false;
            yield break;
        }
        Vector2 position = ClampPosition(new Vector2(spawnPosition.x, spawnPosition.y));
        SpawnFish(PlayerFish[0], position, 1, true, playerId, playerName);
        if (playerId == 0L) localKeyboardRespawnPending = false;
    }

    /// <summary>
    /// 升级时生成对应等级的完整玩家鱼，并回收旧对象，确保场景里只存在一条玩家鱼。
    /// 新对象仍由 SpawnFish 放在 entityRoot 下，玩家ID、昵称、位置和经验规则保持一致。
    /// </summary>
    public FishCore ReplacePlayerFishForLevelUp(FishCore oldFish, int newLevel)
    {
        if (oldFish == null || !oldFish.isPlayer || oldFish.isDead) return null;
        if (PlayerFish == null || PlayerFish.Length == 0) return null;

        int prefabIndex = Mathf.Clamp(newLevel - 1, 0, PlayerFish.Length - 1);
        GameObject levelPrefab = PlayerFish[prefabIndex];
        if (levelPrefab == null)
        {
            Debug.LogError("升级失败：PlayerFish[" + prefabIndex + "] 未配置。");
            return null;
        }

        Vector3 oldPosition = oldFish.transform.position;
        long oldPlayerId = oldFish.playerId;
        string oldPlayerName = oldFish.playerName;
        float oldSpeedMultiplier = oldFish.speedMultiplier;
        float oldBoostMultiplier = oldFish.boostMultiplier;
        bool oldBoosting = oldFish.isBoosting;
        FishCore newFish = SpawnFish(
            levelPrefab,
            new Vector2(oldPosition.x, oldPosition.y),
            newLevel,
            true,
            oldPlayerId,
            oldPlayerName,
            false);

        if (newFish == null) return null;

        // 升级经验已在 FishCore 中清零；继承常规移动/Buff参数和玩家身份。
        newFish.score = 0;
        newFish.currentGrowth = 0;
        newFish.speedMultiplier = oldSpeedMultiplier;
        newFish.SetBoostMultiplier(oldBoostMultiplier);
        newFish.SetBoosting(oldBoosting);
        newFish.UpdateVisuals();
        TransferEquippedVorticesToUpgradedFish(oldFish, newFish);

        // SpawnFish 已将远程玩家字典更新到新对象；本地键盘鱼引用也会在 SpawnFish 中更新。
        // 从活动列表中移除旧对象，再回收到对象池，不调用 Die，避免触发复活流程。
        NotifyFishDespawned(oldFish);
        oldFish.isDead = true;
        oldFish.PrepareUpgradeEffectForDespawn();
        LeanPool.Despawn(oldFish.gameObject);

        return newFish;
    }

    public void QueueUpgradeEffectForSafeDespawn(GameObject effect)
    {
        if (effect == null || pendingUpgradeEffectDespawn.Contains(effect)) return;
        pendingUpgradeEffectDespawn.Add(effect);
    }

    public GameObject SpawnUpgradeEffect(Transform fishRoot)
    {
        if (fishRoot == null || UpgradeEff == null) return null;

        // 升级特效作为玩家鱼的子物体生成：鱼移动、转向或被漩涡吸附时自动跟随。
        GameObject effect = LeanPool.Spawn(UpgradeEff, fishRoot);
        if (effect == null) return null;

        effect.transform.localPosition = upgradeEffectLocalOffset;
        effect.transform.localScale = Vector3.one;

        if (upgradeEffectInFront)
        {
            FishVisuals fishVisuals = fishRoot.GetComponent<FishVisuals>();
            SpriteRenderer fishRenderer = fishVisuals != null ? fishVisuals.spriteRenderer : null;
            if (fishRenderer != null)
            {
                Renderer[] effectRenderers = effect.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < effectRenderers.Length; i++)
                {
                    effectRenderers[i].sortingLayerID = fishRenderer.sortingLayerID;
                    effectRenderers[i].sortingOrder = Mathf.Clamp(fishRenderer.sortingOrder + 1, -32768, 32767);
                }
            }
        }

        ParticleSystem[] particleSystems = effect.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem.MainModule main = particleSystems[i].main;
            main.stopAction = ParticleSystemStopAction.None;
            particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystems[i].Play(false);
        }

        return effect;
    }

    public void SpawnBeEatenEffect(Vector3 pos)
    {
        if (BeEatenEff == null) return;
        GameObject effObj = LeanPool.Spawn(BeEatenEff, entityRoot);
        effObj.transform.position = new Vector3(pos.x, pos.y, ActiveFishPlaneZ);
        LeanPool.Despawn(effObj, 1.5f);
    }

    private Rect GetCentralSpawnRect(float centerRatio)
    {
        float ratio = Mathf.Clamp(centerRatio, 0.1f, 1f);
        float width = Mathf.Max(0.001f, maxBounds.x - minBounds.x);
        float height = Mathf.Max(0.001f, maxBounds.y - minBounds.y);
        Vector2 center = (minBounds + maxBounds) * 0.5f;
        Vector2 size = new Vector2(width * ratio, height * ratio);
        return new Rect(center - size * 0.5f, size);
    }

    private Vector2 GetRandomCentralSpawnPosition(float centerRatio)
    {
        Rect area = GetCentralSpawnRect(centerRatio);
        return new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax));
    }

    private bool IsItemSpawnPositionFree(Vector2 position)
    {
        float minimum = Mathf.Max(0.05f, itemMinSpacing);
        float minimumSquared = minimum * minimum;

        for (int i = 0; i < activeItems.Count; i++)
        {
            GameObject itemObj = activeItems[i];
            if (itemObj == null || !itemObj.activeInHierarchy) continue;
            if (((Vector2)itemObj.transform.position - position).sqrMagnitude < minimumSquared) return false;
        }

        float vortexSquared = itemVortexSpacing * itemVortexSpacing;
        if (vortexSquared > 0f)
        {
            for (int i = 0; i < vortices.Count; i++)
            {
                VortexState vortex = vortices[i];
                if (vortex.visual == null || Time.time >= vortex.expiresAt) continue;
                if ((vortex.position - position).sqrMagnitude < vortexSquared) return false;
            }
        }
        return true;
    }

    private bool TryFindItemSpawnPosition(out Vector2 result)
    {
        int attempts = Mathf.Max(1, itemPlacementAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector2 position = GetRandomCentralSpawnPosition(itemSpawnCenterRatio);
            if (!IsItemSpawnPositionFree(position)) continue;
            result = position;
            return true;
        }

        // 区域不足以放下更多道具时选择暂缓补充，而不是把道具生成在同一坐标。
        result = Vector2.zero;
        return false;
    }

    private bool TryFindVortexSpawnPosition(out Vector2 result)
    {
        // 事件漩涡属于全屏特殊事件：使用完整相机活动边界，道具的位置和间距不参与判断。
        float minVortexSquared = Mathf.Max(0f, vortexMinSpacing) * Mathf.Max(0f, vortexMinSpacing);
        int attempts = Mathf.Max(1, vortexPlacementAttempts);

        for (int i = 0; i < attempts; i++)
        {
            Vector2 position = GetRandomPosition();
            bool overlapsVortex = false;

            // 只检查其他事件漩涡之间的间距；普通道具不会限制事件漩涡生成。
            if (minVortexSquared > 0f)
            {
                for (int j = 0; j < vortices.Count; j++)
                {
                    if (vortices[j].visual == null || Time.time >= vortices[j].expiresAt) continue;
                    if ((vortices[j].position - position).sqrMagnitude < minVortexSquared)
                    {
                        overlapsVortex = true;
                        break;
                    }
                }
            }

            if (overlapsVortex) continue;
            result = ClampPosition(position);
            return true;
        }

        // 全屏范围内仍找不到足够间距时，允许事件漩涡彼此重叠，避免事件空跑。
        result = ClampPosition(GetRandomPosition());
        Debug.LogWarning("[FishManager] 全屏范围内没有满足漩涡之间最小距离的位置，已允许事件漩涡重叠；普通道具位置不参与生成判定。");
        return true;
    }

    private ItemType GetItemTypeByIndex(int index)
    {
        switch (index)
        {
            case 0: return ItemType.SpikeShield;
            case 1: return ItemType.HuntingOrder;
            case 2: return ItemType.SlowTime;
            case 3: return ItemType.Invisibility;
            default: return ItemType.Vortex;
        }
    }

    public void SpawnItem(int itemType, Vector2 pos)
    {
        NormalizeLegacyItemSlots();
        if (itemType < 0 || itemType >= Items.Length || Items[itemType] == null) return;
        if (activeItems.Count >= GetDynamicMaxItemCount()) return;

        ItemType desiredType = GetItemTypeByIndex(itemType);
        for (int i = 0; i < activeItems.Count; i++)
        {
            if (activeItems[i] == null || !activeItems[i].activeInHierarchy) continue;
            ItemBase otherItem = activeItems[i].GetComponent<ItemBase>();
            if (otherItem != null && otherItem.type == desiredType) return;
        }

        Rect area = GetCentralSpawnRect(itemSpawnCenterRatio);
        Vector2 position = new Vector2(Mathf.Clamp(pos.x, area.xMin, area.xMax),
            Mathf.Clamp(pos.y, area.yMin, area.yMax));

        if (!IsItemSpawnPositionFree(position) && !TryFindItemSpawnPosition(out position)) return;

        GameObject itemObj = LeanPool.Spawn(Items[itemType], entityRoot);
        itemObj.transform.position = new Vector3(position.x, position.y, ActiveFishPlaneZ);

        ItemBase itemBase = itemObj.GetComponent<ItemBase>();
        if (itemBase == null)
        {
            Debug.LogWarning("生成的道具预制体没有 ItemBase：" + Items[itemType].name);
            LeanPool.Despawn(itemObj);
            return;
        }

        itemBase.PrepareForWorldSpawn();
        itemBase.type = desiredType;
        activeItems.Remove(itemObj);
        activeItems.Add(itemObj);
    }

    private void ScheduleNextSpecialEvent()
    {
        float minimum = Mathf.Max(1f, Mathf.Min(specialEventIntervalMin, specialEventIntervalMax));
        float maximum = Mathf.Max(minimum, Mathf.Max(specialEventIntervalMin, specialEventIntervalMax));
        nextEventTime = Time.time + Random.Range(minimum, maximum);
    }

    private void TickSpecialEvents()
    {
        if (activeEventType == 0)
        {
            if (Time.time >= nextEventTime) BeginNextSpecialEvent();
            return;
        }

        if (Time.time >= activeEventEndTime)
        {
            EndSpecialEvent();
            return;
        }

        if (activeEventType == 1) TickMadnessDarkness();
    }

    private void BeginNextSpecialEvent()
    {
        // 固定按1疯狂海域 -> 2极光祝福 -> 3漩涡风暴轮换，不再随机抽事件。
        activeEventType = Mathf.Clamp(nextSpecialEventType, 1, 3);
        nextSpecialEventType = activeEventType == 3 ? 1 : activeEventType + 1;
        hasStartedSpecialEvent = true;
        if (activeEventType == 1)
        {
            activeEventEndTime = Time.time + Mathf.Max(1f, madnessEventDuration);
            nextNpcPopulationCheck = Time.time;
            nextDarknessTime = Time.time + Random.Range(0.5f, 2f);
            darknessEndTime = 0f;
        }
        else if (activeEventType == 2)
        {
            activeEventEndTime = Time.time + Mathf.Max(1f, auroraEventDuration);
            StartAuroraVisual();
            RefreshNpcPopulation(true);
        }
        else
        {
            activeEventEndTime = Time.time + Mathf.Max(1f, vortexStormDuration);
            int maxCount = Mathf.Max(1, maxActiveVortices);
            int count = Mathf.Clamp(initialVortexCount, 1, maxCount);
            int spawnedCount = 0;
            Debug.Log("[FishManager] 漩涡风暴开始：Prefab=" +
                (vortexEventPrefab != null ? vortexEventPrefab.name : "未配置，使用备用漩涡") +
                "，初始数量=" + count + "，最大数量=" + maxCount +
                "，生成范围=全屏，事件漩涡最小间距=" + vortexMinSpacing.ToString("F2") +
                "，普通道具间距不参与判定");
            for (int i = 0; i < count; i++)
                if (SpawnVortexEvent()) spawnedCount++;
            if (spawnedCount == 0)
                Debug.LogError("[FishManager] 漩涡风暴已触发，但没有成功生成任何漩涡。请检查前面的Spawn日志、EntityRoot和Prefab组件。");
            ScheduleNextVortex();
        }

        Debug.Log("触发轮换事件：" + GetEventName(activeEventType) + "，下一事件：" +
            GetEventName(nextSpecialEventType) + "，当前普通NPC：" + activeNpcFish.Count +
            "，目标普通NPC：" + GetDesiredNpcCount() +
            (activeEventType == 1 ? "，NPC速度倍率：" + madnessSpeedMultiplier.ToString("F1") : ""));
    }

    private void EndSpecialEvent()
    {
        if (activeEventType == 3)
        {
            for (int i = vortices.Count - 1; i >= 0; i--)
            {
                DespawnVortexEvent(vortices[i]);
                vortices.RemoveAt(i);
            }
        }

        StopMadnessDarkness();
        if (auroraEffectInstance != null)
        {
            ReturnPooledEventVisual(auroraEffectInstance);
            auroraEffectInstance = null;
        }

        bool wasMadness = activeEventType == 1;
        activeEventType = 0;
        activeEventEndTime = 0f;
        if (wasMadness) RefreshNpcPopulation(true); // 疯狂海域结束恢复正常NPC数量。
        ScheduleNextSpecialEvent();
    }

    private void TickMadnessDarkness()
    {
        if (darknessEffectInstance != null && Time.time >= darknessEndTime)
            StopMadnessDarkness();

        if (darknessEffectInstance == null && Time.time >= nextDarknessTime)
        {
            StartMadnessDarkness();
            darknessEndTime = Time.time + Mathf.Max(0.1f, darknessFlashDuration);
            float minimum = Mathf.Max(0.1f, Mathf.Min(darknessFlashIntervalMin, darknessFlashIntervalMax));
            float maximum = Mathf.Max(minimum, Mathf.Max(darknessFlashIntervalMin, darknessFlashIntervalMax));
            nextDarknessTime = darknessEndTime + Random.Range(minimum, maximum);
        }
    }

    private GameObject CreateDarknessFallbackPrefab()
    {
        if (generatedOverlayPrefab != null) return generatedOverlayPrefab;

        generatedOverlayTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Color[] colors = { Color.black, Color.black, Color.black, Color.black };
        generatedOverlayTexture.SetPixels(colors);
        generatedOverlayTexture.Apply();
        generatedOverlaySprite = Sprite.Create(generatedOverlayTexture, new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f), 1f);

        generatedOverlayPrefab = new GameObject("MadnessDarkness_RuntimePrefab");
        generatedOverlayPrefab.transform.position = new Vector3(99999f, 99999f, 99999f);
        SpriteRenderer renderer = generatedOverlayPrefab.AddComponent<SpriteRenderer>();
        renderer.sprite = generatedOverlaySprite;
        renderer.sortingOrder = 32000;
        generatedOverlayPrefab.SetActive(false);
        return generatedOverlayPrefab;
    }

    private void StartMadnessDarkness()
    {
        GameObject prefab = darknessOverlayPrefab != null ? darknessOverlayPrefab : CreateDarknessFallbackPrefab();
        darknessEffectInstance = LeanPool.Spawn(prefab, entityRoot);
        if (darknessEffectInstance == null) return;

        darknessEffectInstance.SetActive(true);
        darknessEffectInstance.transform.position = new Vector3(
            (minBounds.x + maxBounds.x) * 0.5f, (minBounds.y + maxBounds.y) * 0.5f, ActiveFishPlaneZ - 0.05f);
        darknessEffectInstance.transform.localScale = new Vector3(
            Mathf.Max(0.1f, maxBounds.x - minBounds.x) * 1.15f,
            Mathf.Max(0.1f, maxBounds.y - minBounds.y) * 1.15f, 1f);
        // 黑暗遮罩挂在 EntityRoot 下后，使用指定局部 Z 层级覆盖全屏。
        Vector3 darknessLocalPosition = darknessEffectInstance.transform.localPosition;
        darknessLocalPosition.z = -12f;
        darknessEffectInstance.transform.localPosition = darknessLocalPosition;

        darknessRenderer = darknessEffectInstance.GetComponentInChildren<SpriteRenderer>();
        if (darknessRenderer != null)
        {
            darknessRenderer.sortingOrder = 32000;
            Color color = darknessRenderer.color;
            color.a = Mathf.Clamp01(darknessOpacity);
            darknessRenderer.color = color;
        }
        RestartPooledParticles(darknessEffectInstance);
    }

    private void StopMadnessDarkness()
    {
        if (darknessEffectInstance != null)
        {
            ReturnPooledEventVisual(darknessEffectInstance);
            darknessEffectInstance = null;
        }
        darknessRenderer = null;
    }

    private void StartAuroraVisual()
    {
        // 没有配置美术极光Prefab时不生成占位粒子，避免只出现粉色小点。
        if (auroraEventPrefab == null)
        {
            Debug.LogWarning("极光祝福已触发，但未配置 auroraEventPrefab；本次不生成替代粒子特效。");
            return;
        }

        GameObject prefab = auroraEventPrefab;
        auroraEffectInstance = LeanPool.Spawn(prefab, entityRoot);
        if (auroraEffectInstance == null) return;
        auroraEffectInstance.SetActive(true);
        auroraEffectInstance.transform.position = new Vector3(
            (minBounds.x + maxBounds.x) * 0.5f,
            maxBounds.y - (maxBounds.y - minBounds.y) * 0.18f, ActiveFishPlaneZ);
        Collider2D[] colliders = auroraEffectInstance.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
        RestartPooledParticles(auroraEffectInstance);
    }

    private void RestartPooledParticles(GameObject visual)
    {
        if (visual == null) return;
        ParticleSystem[] systems = visual.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.stopAction = ParticleSystemStopAction.None;
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            systems[i].Play(false);
        }
    }

    private void ReturnPooledEventVisual(GameObject visual)
    {
        if (visual == null) return;
        ParticleSystem[] systems = visual.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
            systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        LeanPool.Despawn(visual);
    }

    private GameObject GetVortexPrefabSource()
    {
        if (vortexEventPrefab != null) return vortexEventPrefab;
        return GetOrCreateFallbackVortexPrefab();
    }

    private GameObject GetOrCreateFallbackVortexPrefab()
    {
        if (fallbackVortexPrefab != null) return fallbackVortexPrefab;

        // 没有提供可见Prefab时，创建静态占位图形；不额外旋转，由正式特效自行表现旋转。
        generatedVortexTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        Color[] colors = new Color[64];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                float px = (x - 3.5f) / 3.5f;
                float py = (y - 3.5f) / 3.5f;
                float radius = Mathf.Sqrt(px * px + py * py);
                colors[y * 8 + x] = new Color(0.24f, 0.85f, 1f,
                    Mathf.Clamp01((0.95f - radius) * 1.7f));
            }
        }
        generatedVortexTexture.SetPixels(colors);
        generatedVortexTexture.Apply();
        generatedVortexSprite = Sprite.Create(generatedVortexTexture, new Rect(0, 0, 8, 8),
            new Vector2(0.5f, 0.5f), 8f);

        fallbackVortexPrefab = new GameObject("VortexFallback_RuntimePrefab");
        fallbackVortexPrefab.transform.position = new Vector3(99999f, 99999f, 99999f);
        SpriteRenderer renderer = fallbackVortexPrefab.AddComponent<SpriteRenderer>();
        renderer.sprite = generatedVortexSprite;
        renderer.sortingOrder = 100;
        fallbackVortexPrefab.SetActive(false);
        return fallbackVortexPrefab;
    }

    private void EnsureVortexEventRenderersEnabled(GameObject visual)
    {
        if (visual == null) return;

        // 漩涡特效沿用Prefab本身配置的GameObject Layer、Sorting Layer和Order in Layer。
        // 这里只确保可见节点及Renderer处于启用状态，不强制修改渲染层级。
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            Transform current = renderers[i].transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf) current.gameObject.SetActive(true);
                if (current == visual.transform) break;
                current = current.parent;
            }

            renderers[i].enabled = true;
        }

        if (renderers.Length == 0)
            Debug.LogWarning("[FishManager] 漩涡Prefab中没有找到Renderer，可能没有可见的Sprite/粒子渲染器：" + visual.name);
    }

    private void ScheduleNextVortex()
    {
        float minimum = Mathf.Max(0.5f, Mathf.Min(vortexSpawnIntervalMin, vortexSpawnIntervalMax));
        float maximum = Mathf.Max(minimum, Mathf.Max(vortexSpawnIntervalMin, vortexSpawnIntervalMax));
        float delay = Random.Range(minimum, maximum);
        // 不允许随机间隔超过事件剩余时间太多，避免前一个漩涡消失后直到事件结束都不再补充。
        if (activeEventType == 3 && activeEventEndTime > Time.time + 1f)
            delay = Mathf.Min(delay, Mathf.Max(0.5f, activeEventEndTime - Time.time - 1f));
        nextVortexSpawnTime = Time.time + delay;
    }

    private bool SpawnVortexEvent()
    {
        int maxCount = Mathf.Max(1, maxActiveVortices);
        if (vortices.Count >= maxCount)
        {
            Debug.LogWarning("[FishManager] 漩涡生成跳过：已达到maxActiveVortices=" + maxCount);
            return false;
        }

        Vector2 position;
        if (!TryFindVortexSpawnPosition(out position))
        {
            // 保护性兜底：即使场上道具很多、无法满足间距，也不能让整个事件空白。
            position = ClampPosition((minBounds + maxBounds) * 0.5f);
            Debug.LogWarning("[FishManager] 无法找到满足间距的漩涡位置，改在屏幕中心生成。");
        }

        GameObject prefab = GetVortexPrefabSource();
        if (prefab == null)
        {
            Debug.LogError("[FishManager] 漩涡Prefab和备用Prefab都创建失败，无法生成漩涡。");
            return false;
        }

        GameObject visual = LeanPool.Spawn(prefab, entityRoot);
        if (visual == null)
        {
            Debug.LogError("[FishManager] LeanPool.Spawn返回空对象，Prefab=" + prefab.name + "。请确认该对象是有效Prefab且LeanPool可正常使用。");
            return false;
        }

        // 如果拖入的Prefab只是空容器、没有任何可渲染内容，则回退到可见占位漩涡，避免事件完全看不见。
        Renderer[] prefabRenderers = visual.GetComponentsInChildren<Renderer>(true);
        ParticleSystem[] prefabParticles = visual.GetComponentsInChildren<ParticleSystem>(true);
        if (prefabRenderers.Length == 0 && prefabParticles.Length == 0 && vortexEventPrefab != null)
        {
            Debug.LogWarning("[FishManager] 已指定的漩涡Prefab没有Renderer/ParticleSystem，临时改用可见占位漩涡。Prefab=" + prefab.name);
            LeanPool.Despawn(visual);
            prefab = GetOrCreateFallbackVortexPrefab();
            visual = LeanPool.Spawn(prefab, entityRoot);
            if (visual == null)
            {
                Debug.LogError("[FishManager] 备用漩涡生成失败。");
                return false;
            }
        }

        VortexState state = new VortexState();
        state.id = nextVortexId++;
        if (nextVortexId == int.MaxValue) nextVortexId = 1;
        state.position = position;
        state.direction = Random.insideUnitCircle.normalized;
        if (state.direction.sqrMagnitude < 0.01f) state.direction = Vector2.right;
        float minLifetime = Mathf.Max(0.5f, Mathf.Min(vortexLifetimeMin, vortexLifetimeMax));
        float maxLifetime = Mathf.Max(minLifetime, Mathf.Max(vortexLifetimeMin, vortexLifetimeMax));
        state.expiresAt = Time.time + Random.Range(minLifetime, maxLifetime);
        state.nextTurnTime = Time.time + Random.Range(
            Mathf.Max(0.2f, Mathf.Min(vortexTurnIntervalMin, vortexTurnIntervalMax)),
            Mathf.Max(0.2f, Mathf.Max(vortexTurnIntervalMin, vortexTurnIntervalMax)));
        state.visual = visual;

        visual.SetActive(true);
        visual.transform.position = new Vector3(position.x, position.y, ActiveFishPlaneZ);
        visual.transform.rotation = prefab.transform.rotation;
        if (prefab == fallbackVortexPrefab)
        {
            visual.transform.localScale = Vector3.one * Mathf.Max(0.1f, vortexVisualDiameter);
        }
        else
        {
            Vector3 prefabScale = prefab.transform.localScale;
            // 2D特效的Z缩放若为0，会把部分3D粒子/网格特效压扁；只修正实例，不改Prefab资源。
            if (Mathf.Abs(prefabScale.x) < 0.001f) prefabScale.x = 1f;
            if (Mathf.Abs(prefabScale.y) < 0.001f) prefabScale.y = 1f;
            if (Mathf.Abs(prefabScale.z) < 0.001f) prefabScale.z = 1f;
            visual.transform.localScale = prefabScale * Mathf.Max(0.1f, vortexEventScaleMultiplier);
        }

        // 漩涡事件不参与道具拾取；即使复用道具Prefab也关闭其所有碰撞体。
        Collider2D[] colliders = visual.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = false;

        EnsureVortexEventRenderersEnabled(visual);

        // 每个粒子系统都重新启动，并禁止StopAction自动销毁对象池实例。
        ParticleSystem[] particleSystems = visual.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            if (particleSystems[i] == null) continue;
            ParticleSystem.MainModule main = particleSystems[i].main;
            main.stopAction = ParticleSystemStopAction.None;
            particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystems[i].Play(false);
        }

        if (visual.GetComponentInChildren<ItemBase>(true) != null)
            Debug.LogWarning("[FishManager] 漩涡事件复用了带ItemBase的道具Prefab；碰撞已关闭。如果视觉正常可忽略此提示。");

        vortices.Add(state);
        Debug.Log("[FishManager] 漩涡生成成功：实例=" + visual.name +
            "，位置=" + position.ToString() +
            "，渲染器=" + visual.GetComponentsInChildren<Renderer>(true).Length +
            "，粒子系统=" + particleSystems.Length +
            "，缩放=" + visual.transform.localScale.ToString() +
            "，剩余时间=" + (state.expiresAt - Time.time).ToString("F1") + "秒");
        return true;
    }

    private void DespawnVortexEvent(VortexState state)
    {
        if (state == null || state.visual == null) return;

        GameObject visual = state.visual;
        state.visual = null;
        ParticleSystem[] particleSystems = visual.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
            particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        LeanPool.Despawn(visual);
    }

    private void TickVortexEvents()
    {
        int maxCount = Mathf.Max(1, maxActiveVortices);
        for (int i = vortices.Count - 1; i >= 0; i--)
        {
            VortexState state = vortices[i];
            if (state.visual == null || Time.time >= state.expiresAt || i >= maxCount)
            {
                DespawnVortexEvent(state);
                vortices.RemoveAt(i);
                continue;
            }

            if (Time.time >= state.nextTurnTime)
            {
                float angle = Random.Range(-85f, 85f) * Mathf.Deg2Rad;
                float sine = Mathf.Sin(angle);
                float cosine = Mathf.Cos(angle);
                state.direction = new Vector2(state.direction.x * cosine - state.direction.y * sine,
                    state.direction.x * sine + state.direction.y * cosine).normalized;
                state.nextTurnTime = Time.time + Random.Range(
                    Mathf.Max(0.2f, Mathf.Min(vortexTurnIntervalMin, vortexTurnIntervalMax)),
                    Mathf.Max(0.2f, Mathf.Max(vortexTurnIntervalMin, vortexTurnIntervalMax)));
            }

            Vector2 next = state.position + state.direction * Mathf.Max(0f, vortexMoveSpeed) * Time.fixedDeltaTime;
            bool hitLeft = next.x < minBounds.x;
            bool hitRight = next.x > maxBounds.x;
            bool hitBottom = next.y < minBounds.y;
            bool hitTop = next.y > maxBounds.y;

            if (hitLeft || hitRight) state.direction.x *= -1f;
            if (hitBottom || hitTop) state.direction.y *= -1f;

            state.position = ClampPosition(next);
            state.visual.transform.position = new Vector3(state.position.x, state.position.y, ActiveFishPlaneZ);
        }
    }

    public bool TryGetVortexPosition(int vortexId, out Vector2 center)
    {
        center = Vector2.zero;
        if (vortexId <= 0) return false;

        for (int i = 0; i < vortices.Count; i++)
        {
            VortexState state = vortices[i];
            if (state.id != vortexId || state.visual == null || Time.time >= state.expiresAt) continue;
            center = state.position;
            return true;
        }

        return false;
    }

    public bool TryGetVortexCaptureTarget(FishCore fish, Vector2 fishPosition, out int vortexId, out Vector2 center)
    {
        vortexId = -1;
        center = Vector2.zero;
        if (fish == null || fish.isDead || vortices.Count == 0) return false;
        if (fish.isPlayer && !vortexAffectsPlayers) return false;
        if (!fish.isPlayer && fish.specialFishType < 0 && !vortexAffectsNpc) return false;
        if (!fish.isPlayer && fish.specialFishType >= 0 && !vortexAffectsSpecialFish) return false;

        float radius = Mathf.Max(0.1f, vortexAttractionRadius);
        float closestDistance = radius * radius;

        for (int i = 0; i < vortices.Count; i++)
        {
            VortexState state = vortices[i];
            if (state.visual == null || Time.time >= state.expiresAt) continue;
            float sqrDistance = (state.position - fishPosition).sqrMagnitude;
            if (sqrDistance > closestDistance) continue;
            closestDistance = sqrDistance;
            vortexId = state.id;
            center = state.position;
        }
        return vortexId > 0;
    }

    // 保留原接口，防止你工程中其他脚本使用时找不到这个方法。
    public bool TryGetVortexInfluence(FishCore fish, Vector2 fishPosition, out Vector2 vortexCenter)
    {
        int vortexId;
        return TryGetVortexCaptureTarget(fish, fishPosition, out vortexId, out vortexCenter);
    }

    // 运行时调参面板由FishManagerRuntimeConfig独立组件绘制和保存；这里仅提供状态/动作接口。
    public string GetRuntimeStatusText()
    {
        int remaining = Mathf.CeilToInt(Mathf.Max(0f,
            (activeEventType == 0 ? nextEventTime : activeEventEndTime) - Time.time));
        return "当前：" + GetEventName(activeEventType) + " | 下一次：" + GetEventName(nextSpecialEventType) +
            " | 倒计时：" + remaining + "秒 | NPC：" + activeNpcFish.Count + "/" + GetDesiredNpcCount() +
            " | 非玩家速度：x" + NonPlayerEventSpeedMultiplier.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void RequestRuntimeNextEvent()
    {
        if (activeEventType != 0) EndSpecialEvent();
        nextEventTime = Time.time;
    }

    public void NotifyRuntimeConfigApplied()
    {
        if (mainCamera != null) CalculateScreenBounds();
        nextNpcPopulationCheck = Mathf.Min(nextNpcPopulationCheck, Time.time + 0.1f);
        nextItemSpawnTime = Mathf.Min(nextItemSpawnTime, Time.time + 0.1f);
        if (!hasStartedSpecialEvent) nextSpecialEventType = Mathf.Clamp(firstSpecialEventType, 1, 3);
    }

    // 特殊事件名称转换，供游戏事件日志使用。
    // Prefab、Transform、Camera 和数组只能由 Unity Inspector 绑定，运行时只读显示，避免丢失引用。
    private static string GetEventName(int type)
    {
        if (type == 1) return "疯狂海域";
        if (type == 2) return "极光祝福";
        if (type == 3) return "漩涡风暴";
        return "等待中";
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        for (int i = vortices.Count - 1; i >= 0; i--) DespawnVortexEvent(vortices[i]);
        vortices.Clear();
        for (int i = activeItemVortices.Count - 1; i >= 0; i--)
            if (activeItemVortices[i].visual != null) ReturnPooledEventVisual(activeItemVortices[i].visual);
        activeItemVortices.Clear();
        for (int i = 0; i < pendingEquippedVortexDespawn.Count; i++)
            if (pendingEquippedVortexDespawn[i] != null)
                ReturnPooledEventVisual(pendingEquippedVortexDespawn[i]);
        pendingEquippedVortexDespawn.Clear();
        activeSlowFields.Clear();

        StopMadnessDarkness();
        if (auroraEffectInstance != null) ReturnPooledEventVisual(auroraEffectInstance);
        if (fallbackVortexPrefab != null) Destroy(fallbackVortexPrefab);
        if (generatedVortexSprite != null) Destroy(generatedVortexSprite);
        if (generatedVortexTexture != null) Destroy(generatedVortexTexture);
        if (generatedOverlayPrefab != null) Destroy(generatedOverlayPrefab);
        if (generatedOverlaySprite != null) Destroy(generatedOverlaySprite);
        if (generatedOverlayTexture != null) Destroy(generatedOverlayTexture);
    }

    private void OnEnable() { MyMqtt.MainThreadMessageReceived += OnMqttMainThreadMessage; }
    private void OnDisable() { MyMqtt.MainThreadMessageReceived -= OnMqttMainThreadMessage; }

    private void OnMqttMainThreadMessage(MQTTMsg msg)
    {
        if (msg == null || msg.CommandType != MsgType.小鱼游戏 || string.IsNullOrWhiteSpace(msg.Data)) return;

        JObject data;
        try
        {
            data = JObject.Parse(msg.Data);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("小鱼游戏 MQTT Data 不是有效 JSON：" + ex.Message + "；Data=" + msg.Data);
            return;
        }

        if (data["subcmd"] == null)
        {
            Debug.LogWarning("小鱼游戏 MQTT 消息缺少 subcmd：" + msg.Data);
            return;
        }

        int subcmd = data["subcmd"].Value<int>();
        Debug.Log("收到小鱼游戏 MQTT：subcmd=" + subcmd + "，Data=" + msg.Data);

        // 接口文档定义 subcmd=2 为离场；兼容服务端误标记的方向消息。
        // 只有 subcmd=2 同时带 angle/force/active 时，才按方向处理。
        bool containsDirection = data["angle"] != null && data["force"] != null && data["active"] != null;
        if (subcmd == 2 && containsDirection)
        {
            Debug.LogWarning("收到 subcmd=2 但 Data 包含 angle/force/active，按方向消息兼容处理。请核对服务端协议。");
            HandleMqttDirection(data.ToObject<SC_小鱼游戏_方向>());
            return;
        }

        switch (subcmd)
        {
            case 1:
                HandleMqttJoin(data.ToObject<SC_小鱼游戏_进场>());
                break;
            case 2:
                HandleMqttLeave(data.ToObject<SC_小鱼游戏_离场>());
                break;
            case 3:
                HandleMqttDirection(data.ToObject<SC_小鱼游戏_方向>());
                break;
            case 4:
                HandleMqttBoost(data.ToObject<SC_小鱼游戏_加速>());
                break;
            default:
                Debug.LogWarning("未知的小鱼游戏 subcmd=" + subcmd + "；Data=" + msg.Data);
                break;
        }
    }

    private void HandleMqttJoin(SC_小鱼游戏_进场 command)
    {
        if (command == null || command.playerId <= 0)
        {
            Debug.LogWarning("进场消息缺少有效 playerId。");
            return;
        }

        pendingRemoteRespawns.Remove(command.playerId);
        if (remotePlayerFish.TryGetValue(command.playerId, out FishCore existing))
        {
            if (existing != null && existing.gameObject.activeInHierarchy)
            {
                existing.playerName = string.IsNullOrEmpty(command.nickName) ? "玩家" : command.nickName;
                existing.UpdateVisuals();
                return;
            }
            remotePlayerFish.Remove(command.playerId);
        }

        if (PlayerFish == null || PlayerFish.Length == 0 || PlayerFish[0] == null)
        {
            Debug.LogError("收到玩家进场消息，但 FishManager.PlayerFish[0] 未配置。");
            return;
        }

        Vector2 center = (minBounds + maxBounds) * 0.5f;
        string nickName = string.IsNullOrEmpty(command.nickName) ? "玩家" : command.nickName;
        SpawnFish(PlayerFish[0], center, 1, true, command.playerId, nickName);
        SpawnInitialEntities();
        Debug.Log("玩家进场：" + nickName + "，playerId=" + command.playerId);
    }

    private void HandleMqttLeave(SC_小鱼游戏_离场 command)
    {
        if (command == null || command.playerId <= 0)
        {
            Debug.LogWarning("离场消息缺少有效 playerId。");
            return;
        }

        if (!remotePlayerFish.TryGetValue(command.playerId, out FishCore fish))
        {
            if (pendingRemoteRespawns.Remove(command.playerId))
                Debug.Log("已取消离场玩家的待执行复活，playerId=" + command.playerId);
            else
                Debug.LogWarning("收到离场消息，但找不到玩家 playerId=" + command.playerId);
            return;
        }

        remotePlayerFish.Remove(command.playerId);
        if (fish != null && fish.gameObject.activeInHierarchy)
        {
            fish.isDead = true; // 离场不触发死亡复活流程
            ReleaseEquippedVorticesForOwner(fish);
            fish.PrepareUpgradeEffectForDespawn();
            LeanPool.Despawn(fish.gameObject);
        }
        Debug.Log("玩家离场：playerId=" + command.playerId);
    }

    private void HandleMqttDirection(SC_小鱼游戏_方向 command)
    {
        if (command == null || command.playerId <= 0) return;
        if (remotePlayerFish.TryGetValue(command.playerId, out FishCore fish))
        {
            if (fish != null && fish.gameObject.activeInHierarchy)
            {
                PlayerInputMovement inputComp = fish.GetComponent<PlayerInputMovement>();
                if (inputComp != null) inputComp.SetRemoteJoystick(command.angle, command.force, command.active);
                fish.MarkInitialMqttControlReceived();
            }
            else remotePlayerFish.Remove(command.playerId);
        }
        else
        {
            Debug.LogWarning("方向消息没有匹配的玩家鱼，playerId=" + command.playerId + "。请确认先收到 subcmd=1 进场消息。");
        }
    }

    private void HandleMqttBoost(SC_小鱼游戏_加速 command)
    {
        if (command == null || command.playerId <= 0) return;
        if (remotePlayerFish.TryGetValue(command.playerId, out FishCore fish))
        {
            if (fish != null && fish.gameObject.activeInHierarchy)
            {
                fish.SetBoosting(command.pressed);
                fish.MarkInitialMqttControlReceived();
            }
            else
                remotePlayerFish.Remove(command.playerId);
        }
        else
        {
            Debug.LogWarning("加速消息没有匹配的玩家鱼，playerId=" + command.playerId + "。请确认先收到 subcmd=1 进场消息。");
        }
    }
}