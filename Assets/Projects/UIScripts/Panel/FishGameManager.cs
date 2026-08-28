using System.Collections.Generic;
using Lean.Pool;
using UnityEngine;
using System.Collections;
using DG.Tweening;
using MqttData;
using Newtonsoft.Json.Linq;

/// <summary>
/// 大鱼吃小鱼游戏管理器。
///
/// 鱼的强弱规则：
/// 1. FishList 会按照鱼 Prefab 的 RectTransform.sizeDelta 面积从小到大排序。
/// 2. 排序后的下标就是强度下标，等级 = 下标 + 1。
/// 3. 强度高的鱼可以吃强度低的鱼。
/// 4. 吃够指定数量后升级为 FishList 中的下一种鱼。
/// </summary>
public class FishGameManager : MonoBehaviour
{
    public static FishGameManager Instance { get; private set; }

    [Header("鱼的父物体")]
    [Tooltip("所有通过对象池生成的鱼都会放到这个节点下")]
    public RectTransform fishContainer;

    [Header("鱼的活动区域")]
    [Tooltip("鱼生成和移动的 UI 区域，不设置时使用 fishContainer")]
    public RectTransform moveArea;

    [Header("鱼消失特效（世界坐标）")]
    [Tooltip("鱼被吃掉时播放的世界空间特效 Prefab")]
    public GameObject fishDisappearEffectPrefab;

    [Tooltip("玩家升级时播放的升级特效 Prefab，坐标规则与鱼消失特效一致")]
    public GameObject fishUpgradeEffectPrefab;

    [Tooltip("用于把 UI 鱼的位置转换到世界坐标。未设置时优先使用 Canvas 的 World Camera，其次使用 Main Camera")]
    public Camera worldEffectCamera;

    [Tooltip("世界特效的父节点。不设置时直接生成在 Hierarchy 最外层")]
    public Transform worldEffectRoot;

    [Tooltip("世界特效所在的 Z 平面。正交相机位于 Z=-10 时，通常设置为 0")]
    public float worldEffectZ = 0f;

    [Tooltip("特效存活时间，结束后自动销毁")]
    [Min(0.05f)]
    public float fishDisappearEffectLifeTime = 2f;

    [Tooltip("玩家升级、旧鱼消失时是否也播放该特效")]
    public bool playEffectWhenUpgrade = true;

    [Header("随机水泡特效（世界坐标）")]
    [Tooltip("是否自动随机生成水泡特效")]
    public bool spawnBubbleEffects = true;

    [Tooltip("可以配置多个水泡预制体，每次随机选择一个生成")]
    public List<GameObject> bubbleEffectPrefabs =
        new List<GameObject>();

    [Tooltip("水泡生成的最小时间间隔。与最大值相同即可使用固定间隔")]
    [Min(0.05f)]
    public float bubbleSpawnIntervalMin = 1.5f;

    [Tooltip("水泡生成的最大时间间隔。与最小值相同即可使用固定间隔")]
    [Min(0.05f)]
    public float bubbleSpawnIntervalMax = 3f;

    [Tooltip("水泡距离屏幕边缘的比例，0.05 表示保留 5% 的边距")]
    [Range(0f, 0.49f)]
    public float bubbleViewportPadding = 0.05f;

    [Tooltip("水泡只在屏幕底部这部分高度内生成。0.5 表示只在下半屏生成")]
    [Range(0.05f, 1f)]
    public float bubbleSpawnHeightRatio = 0.5f;

    [Tooltip("水泡生成后存在的时间，与鱼消失特效时间相互独立")]
    [Min(0.05f)]
    public float bubbleEffectLifeTime = 4f;

    [Tooltip("水泡特效的父节点。不设置时使用 World Effect Root")]
    public Transform bubbleEffectRoot;

    [Header("鱼等级列表")]
    [Tooltip("运行时会按照 RectTransform.sizeDelta 的面积从小到大排序。下标 0 对应等级 1")]
    public List<GameObject> FishList = new List<GameObject>();

    [Header("升级条件")]
    [Tooltip("等级 1 升级需要吃掉的鱼数量")]
    [Min(1)]
    public int baseUpgradeEatCount = 3;

    [Tooltip("每提高 1 个等级，升级额外需要吃掉的鱼数量。例如基础 3、每级增加 2，则各等级需要 3、5、7、9...")]
    [Min(0)]
    public int additionalEatCountPerLevel = 2;

    [Tooltip("升级所需吃鱼数量的最大值。设置为 0 表示不限制")]
    [Min(0)]
    public int maxUpgradeEatCount = 0;

    [Header("初始鱼群")]
    [Tooltip("游戏开始时生成的 AI 鱼总数")]
    [Min(0)]
    public int initialFishCount = 20;

    [Tooltip("生成的鱼中，低于玩家等级的比例。0.5 表示一半")]
    [Range(0f, 1f)]
    public float lowerThanPlayerSpawnRatio = 0.5f;

    [Tooltip("剩余鱼群中，生成高于玩家等级鱼的概率。默认 0.2，整体约只有 10% 比玩家高")]
    [Range(0f, 1f)]
    public float higherThanPlayerChance = 0.2f;

    [Tooltip("生成的鱼最多允许比玩家高多少级。建议保持为 1")]
    [Min(0)]
    public int maxSpawnLevelAbovePlayer = 1;

    [Header("生成设置")]
    [Tooltip("鱼生成时距离区域边缘的距离")]
    [Min(0f)]
    public float spawnPadding = 60f;

    [Tooltip("运行游戏后是否自动生成鱼")]
    public bool spawnOnStart = true;

    [Header("玩家鱼")]
    [Tooltip("玩家初始等级，从 1 开始。默认等级 2")]
    [Min(1)]
    public int initialPlayerLevel = 2;

    [Tooltip("备用玩家鱼 Prefab。正常情况下会按照 Initial Player Level 从完整成长列表中选择")]
    public GameObject initialPlayerFishPrefab;

    [Tooltip("玩家鱼的初始生成位置")]
    public Vector2 playerSpawnPosition = Vector2.zero;

    public FishMove PlayerFish { get; private set; }

    [Header("本地 / MQTT 玩家")]
    [Tooltip("是否在 Start 时自动生成本地键盘玩家。关闭后按 F1 创建。")]
    public bool spawnLocalPlayerOnStart = false;

    [Tooltip("创建本地键盘玩家鱼的按键")]
    public KeyCode localPlayerSpawnKey = KeyCode.F1;

    [Tooltip("是否接受 MQTT Join。可在局未开始/结算阶段关闭。")]
    public bool allowRemoteJoin = true;

    [Tooltip("远程 MQTT 玩家人数上限")]
    [Min(1)]
    public int maxRemotePlayers = 8;

    [Tooltip("allowRemoteJoin=false 时返回给小程序的拒绝原因")]
    public string remoteJoinRejectMessage = "本局未开始";

    [Tooltip("是否输出小鱼 MQTT 调试日志")]
    public bool logFishMqtt = false;

    private readonly Dictionary<long, FishMove> remotePlayerFish =
        new Dictionary<long, FishMove>();

    public int RemotePlayerCount
    {
        get { return remotePlayerFish.Count; }
    }

    /// <summary>
    /// 所有鱼 Prefab 拼接后的完整成长列表。
    /// 顺序越靠后，鱼越强。
    /// </summary>
    private readonly List<FishPrefabData> allFish =
        new List<FishPrefabData>();

    [Header("鱼群自动补充")]

    [Tooltip("有鱼被吃掉后，是否自动补充新鱼")]
    public bool autoRespawnFish = true;

    [Tooltip("鱼被吃掉后，等待多少秒补充")]
    [Min(0f)]
    public float respawnDelay = 1.5f;

    [Tooltip("同一时间最多等待生成多少条鱼，防止瞬间大量补鱼")]
    [Min(1)]
    public int maxPendingRespawns = 10;

    private int pendingRespawnCount;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (fishContainer == null)
        {
            fishContainer = transform as RectTransform;
        }

        if (moveArea == null)
        {
            moveArea = fishContainer;
        }

        BuildFishProgression();
    }

    private void OnEnable()
    {
        MyMqtt.MainThreadMessageReceived +=
            OnMqttMainThreadMessage;
    }

    private void OnDisable()
    {
        MyMqtt.MainThreadMessageReceived -=
            OnMqttMainThreadMessage;
    }

    private void Start()
    {
        if (spawnOnStart)
        {
            if (spawnLocalPlayerOnStart)
            {
                SpawnPlayerFish();
            }

            SpawnInitialFish();
        }

        if (spawnBubbleEffects)
        {
            StartCoroutine(
                BubbleSpawnCoroutine()
            );
        }
    }

    private void Update()
    {
        if (localPlayerSpawnKey != KeyCode.None &&
            Input.GetKeyDown(localPlayerSpawnKey))
        {
            SpawnPlayerFish();
        }
    }

    /// <summary>
    /// 按鱼 Prefab 的 RectTransform.sizeDelta 面积从小到大建立等级序列。
    /// 排序后 FishList[0] 是等级 1，FishList[1] 是等级 2，以此类推。
    /// </summary>
    private void BuildFishProgression()
    {
        allFish.Clear();

        if (FishList == null)
        {
            FishList = new List<GameObject>();
        }

        // 移除空引用，避免排序和生成时出现无效项。
        FishList.RemoveAll(prefab => prefab == null);

        FishList.Sort(CompareFishPrefabSize);

        for (int i = 0; i < FishList.Count; i++)
        {
            FishPrefabData data = new FishPrefabData();
            data.prefab = FishList[i];
            data.globalPowerIndex = i;
            allFish.Add(data);
        }

        if (allFish.Count == 0)
        {
            Debug.LogWarning(
                "[FishGameManager] FishList 中没有配置任何有效的鱼 Prefab。"
            );
        }
    }

    private static int CompareFishPrefabSize(
        GameObject fishA,
        GameObject fishB)
    {
        Vector2 sizeA = GetFishSizeDelta(fishA);
        Vector2 sizeB = GetFishSizeDelta(fishB);

        float areaA = Mathf.Abs(sizeA.x * sizeA.y);
        float areaB = Mathf.Abs(sizeB.x * sizeB.y);

        int areaCompare = areaA.CompareTo(areaB);

        if (areaCompare != 0)
        {
            return areaCompare;
        }

        // 面积相同时用宽、高做次级排序。
        int widthCompare =
            Mathf.Abs(sizeA.x).CompareTo(
                Mathf.Abs(sizeB.x)
            );

        if (widthCompare != 0)
        {
            return widthCompare;
        }

        return Mathf.Abs(sizeA.y).CompareTo(
            Mathf.Abs(sizeB.y)
        );
    }

    private static Vector2 GetFishSizeDelta(
        GameObject fishPrefab)
    {
        if (fishPrefab == null)
        {
            return Vector2.zero;
        }

        RectTransform fishRect =
            fishPrefab.GetComponent<RectTransform>();

        if (fishRect == null)
        {
            Debug.LogWarning(
                "[FishGameManager] 鱼 Prefab 缺少 RectTransform：" +
                fishPrefab.name,
                fishPrefab
            );

            return Vector2.zero;
        }

        return fishRect.sizeDelta;
    }

    /// <summary>
    /// 游戏开始时自动生成一条玩家鱼。
    /// </summary>
    public FishMove SpawnPlayerFish()
    {
        if (PlayerFish != null &&
            PlayerFish.gameObject.activeInHierarchy)
        {
            return PlayerFish;
        }

        GameObject prefab = GetInitialPlayerPrefab();

        if (prefab == null)
        {
            Debug.LogError(
                "[FishGameManager] 无法生成玩家鱼。请至少在鱼的成长列表中配置一条鱼，" +
                "或设置备用的 Initial Player Fish Prefab。"
            );
            return null;
        }

        PlayerFish = SpawnFish(
            prefab,
            playerSpawnPosition,
            true
        );

        return PlayerFish;
    }

    /// <summary>
    /// 生成初始鱼群。
    /// 默认一半低于玩家等级；其余以同级鱼为主，
    /// 只有少量鱼会比玩家高，并且不会超过设定的等级差。
    /// </summary>
    public void SpawnInitialFish()
    {
        if (initialFishCount <= 0 || allFish.Count == 0)
        {
            return;
        }

        if (spawnLocalPlayerOnStart &&
            (PlayerFish == null ||
             !PlayerFish.gameObject.activeInHierarchy))
        {
            SpawnPlayerFish();
        }

        int playerPowerIndex =
            GetCurrentPlayerPowerIndex();

        float lowerRatio =
            Mathf.Clamp01(
                lowerThanPlayerSpawnRatio
            );

        int lowerFishCount =
            playerPowerIndex > 0
                ? Mathf.CeilToInt(
                    initialFishCount *
                    lowerRatio
                )
                : 0;

        lowerFishCount =
            Mathf.Clamp(
                lowerFishCount,
                0,
                initialFishCount
            );

        // 先保证指定比例的鱼严格低于玩家等级。
        for (int i = 0; i < lowerFishCount; i++)
        {
            GameObject prefab =
                GetRandomPrefabByPowerIndexRange(
                    0,
                    playerPowerIndex - 1
                );

            if (prefab == null)
            {
                prefab = GetAnyAvailableFishPrefab();
            }

            SpawnFish(
                prefab,
                GetRandomSpawnPosition()
            );
        }

        // 剩余鱼群以玩家同级为主，只生成固定数量的高一级鱼，
        // 避免随机波动导致某一局高等级鱼突然过多。
        int remainingCount =
            initialFishCount -
            lowerFishCount;

        int maxHigherIndex =
            Mathf.Min(
                allFish.Count - 1,
                playerPowerIndex +
                Mathf.Max(
                    0,
                    maxSpawnLevelAbovePlayer
                )
            );

        bool canSpawnHigher =
            maxHigherIndex >
            playerPowerIndex;

        int higherFishCount =
            canSpawnHigher
                ? Mathf.RoundToInt(
                    remainingCount *
                    Mathf.Clamp01(
                        higherThanPlayerChance
                    )
                )
                : 0;

        higherFishCount =
            Mathf.Clamp(
                higherFishCount,
                0,
                remainingCount
            );

        int sameLevelFishCount =
            remainingCount -
            higherFishCount;

        for (int i = 0; i < sameLevelFishCount; i++)
        {
            GameObject prefab =
                GetRandomPrefabByPowerIndexRange(
                    playerPowerIndex,
                    playerPowerIndex
                );

            if (prefab == null)
            {
                prefab = GetAnyAvailableFishPrefab();
            }

            SpawnFish(
                prefab,
                GetRandomSpawnPosition()
            );
        }

        for (int i = 0; i < higherFishCount; i++)
        {
            GameObject prefab =
                GetRandomPrefabByPowerIndexRange(
                    playerPowerIndex + 1,
                    maxHigherIndex
                );

            if (prefab == null)
            {
                prefab =
                    GetRandomPrefabByPowerIndexRange(
                        playerPowerIndex,
                        playerPowerIndex
                    );
            }

            SpawnFish(
                prefab,
                GetRandomSpawnPosition()
            );
        }
    }

    /// <summary>
    /// 使用 Lean Pool 生成一条鱼。兼容旧调用：isPlayer=true 为本地键盘玩家。
    /// </summary>
    public FishMove SpawnFish(
        GameObject prefab,
        Vector2 anchoredPosition,
        bool isPlayer = false)
    {
        return SpawnControlledFish(
            prefab,
            anchoredPosition,
            isPlayer
                ? FishControlMode.LocalKeyboard
                : FishControlMode.AI,
            0L,
            string.Empty
        );
    }

    /// <summary>
    /// 按指定控制模式生成鱼。MQTT 玩家不会读取键盘输入。
    /// </summary>
    public FishMove SpawnControlledFish(
        GameObject prefab,
        Vector2 anchoredPosition,
        FishControlMode controlMode,
        long playerId = 0L,
        string nickName = "")
    {
        if (prefab == null)
        {
            return null;
        }

        if (fishContainer == null)
        {
            Debug.LogError(
                "[FishGameManager] Fish Container 没有设置。"
            );

            return null;
        }

        GameObject fishObject = LeanPool.Spawn(
            prefab,
            fishContainer,
            false
        );

        if (fishObject == null)
        {
            Debug.LogError(
                "[FishGameManager] LeanPool 生成鱼失败：" +
                prefab.name
            );

            return null;
        }

        RectTransform fishRect =
            fishObject.GetComponent<RectTransform>();

        if (fishRect != null)
        {
            fishRect.anchoredPosition3D =
                new Vector3(
                    anchoredPosition.x,
                    anchoredPosition.y,
                    0f
                );

            fishRect.localRotation = Quaternion.identity;
        }
        else
        {
            fishObject.transform.localPosition =
                new Vector3(
                    anchoredPosition.x,
                    anchoredPosition.y,
                    0f
                );
        }

        FishMove fish =
            fishObject.GetComponent<FishMove>();

        if (fish == null)
        {
            Debug.LogError(
                "[FishGameManager] 鱼 Prefab 没有 FishMove：" +
                prefab.name,
                fishObject
            );

            LeanPool.Despawn(fishObject);
            return null;
        }

        int progressionIndex =
            FindPrefabProgressionIndex(prefab);

        if (progressionIndex < 0)
        {
            Debug.LogError(
                "[FishGameManager] 这个 Prefab 不在 FishList 中：" +
                prefab.name,
                prefab
            );

            LeanPool.Despawn(fishObject);
            return null;
        }

        FishPrefabData data =
            allFish[progressionIndex];

        fish.InitializeFish(
            this,
            data.globalPowerIndex,
            GetUpgradeEatCount(data.globalPowerIndex),
            controlMode,
            playerId,
            nickName
        );

        if (controlMode == FishControlMode.LocalKeyboard)
        {
            fish.SetMoveInput(Vector2.zero, 0f);
            PlayerFish = fish;
            fish.transform.SetAsLastSibling();
        }
        else if (controlMode == FishControlMode.RemoteMqtt)
        {
            fish.SetMoveInput(Vector2.zero, 0f);
            fish.transform.SetAsLastSibling();
        }
        else
        {
            fish.SetRandomMoveDirection();
        }

        fish.ResumeMovement();
        return fish;
    }

    private void OnMqttMainThreadMessage(MQTTMsg msg)
    {
        if (msg == null ||
            msg.CommandType != MsgType.小鱼游戏 ||
            string.IsNullOrWhiteSpace(msg.Data))
        {
            return;
        }

        JObject data;

        try
        {
            data = JObject.Parse(msg.Data);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(
                "[FishGameManager] 0x14 Data JSON 解析失败：" +
                ex.Message
            );
            return;
        }

        JToken subcmdToken = data["subcmd"];

        if (subcmdToken == null)
        {
            Debug.LogWarning(
                "[FishGameManager] 0x14 Data 缺少 subcmd。"
            );
            return;
        }

        int subcmd;

        if (!int.TryParse(
            subcmdToken.ToString(),
            out subcmd))
        {
            return;
        }

        try
        {
            switch (subcmd)
            {
                case 1:
                    HandleRemoteJoin(
                        data.ToObject<SC_小鱼游戏_进场>()
                    );
                    break;

                case 2:
                    HandleRemoteLeave(
                        data.ToObject<SC_小鱼游戏_离场>()
                    );
                    break;

                case 3:
                    HandleRemoteDirection(
                        data.ToObject<SC_小鱼游戏_方向>()
                    );
                    break;

                case 4:
                    HandleRemoteBoost(
                        data.ToObject<SC_小鱼游戏_加速>()
                    );
                    break;

                default:
                    if (logFishMqtt)
                    {
                        Debug.LogWarning(
                            "[FishGameManager] 未知小鱼 subcmd=" +
                            subcmd
                        );
                    }
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning(
                "[FishGameManager] 小鱼 subcmd=" +
                subcmd +
                " 数据转换失败：" +
                ex.Message
            );
        }
    }

    private void HandleRemoteJoin(
        SC_小鱼游戏_进场 command)
    {
        if (command == null)
        {
            return;
        }

        long playerId = command.playerId;

        if (playerId <= 0L)
        {
            SendJoinAck(playerId, false, "playerId无效");
            return;
        }

        CleanupRemotePlayers();

        FishMove existing;

        if (remotePlayerFish.TryGetValue(
            playerId,
            out existing) &&
            existing != null &&
            existing.gameObject.activeInHierarchy)
        {
            SendJoinAck(playerId, true, "ok");
            return;
        }

        remotePlayerFish.Remove(playerId);

        if (!allowRemoteJoin)
        {
            SendJoinAck(
                playerId,
                false,
                string.IsNullOrWhiteSpace(
                    remoteJoinRejectMessage)
                    ? "当前暂不可进场"
                    : remoteJoinRejectMessage
            );
            return;
        }

        if (remotePlayerFish.Count >=
            Mathf.Max(1, maxRemotePlayers))
        {
            SendJoinAck(
                playerId,
                false,
                "本局人数已满"
            );
            return;
        }

        GameObject prefab = GetInitialPlayerPrefab();

        if (prefab == null)
        {
            SendJoinAck(
                playerId,
                false,
                "设备未配置玩家鱼"
            );
            return;
        }

        FishMove fish = SpawnControlledFish(
            prefab,
            GetMqttPlayerSpawnPosition(),
            FishControlMode.RemoteMqtt,
            playerId,
            command.nickName
        );

        if (fish == null)
        {
            SendJoinAck(
                playerId,
                false,
                "创建玩家失败"
            );
            return;
        }

        remotePlayerFish[playerId] = fish;
        SendJoinAck(playerId, true, "ok");

        if (logFishMqtt)
        {
            Debug.Log(
                "[FishGameManager] Join playerId=" +
                playerId +
                " nickName=" +
                command.nickName
            );
        }
    }

    private void HandleRemoteLeave(
        SC_小鱼游戏_离场 command)
    {
        if (command == null)
        {
            return;
        }

        FishMove fish;

        if (!remotePlayerFish.TryGetValue(
            command.playerId,
            out fish))
        {
            return;
        }

        remotePlayerFish.Remove(command.playerId);

        if (fish != null &&
            fish.gameObject.activeInHierarchy)
        {
            LeanPool.Despawn(fish.gameObject);
        }
    }

    private void HandleRemoteDirection(
        SC_小鱼游戏_方向 command)
    {
        if (command == null)
        {
            return;
        }

        FishMove fish;

        if (TryGetRemotePlayerFish(
            command.playerId,
            out fish))
        {
            fish.SetRemoteJoystick(
                command.angle,
                command.force,
                command.active
            );
        }
    }

    private void HandleRemoteBoost(
        SC_小鱼游戏_加速 command)
    {
        if (command == null)
        {
            return;
        }

        FishMove fish;

        if (TryGetRemotePlayerFish(
            command.playerId,
            out fish))
        {
            fish.SetExternalBoostPressed(
                command.pressed
            );
        }
    }

    private bool TryGetRemotePlayerFish(
        long playerId,
        out FishMove fish)
    {
        if (remotePlayerFish.TryGetValue(
            playerId,
            out fish) &&
            fish != null &&
            fish.gameObject.activeInHierarchy &&
            fish.IsRemoteMqttPlayer)
        {
            return true;
        }

        remotePlayerFish.Remove(playerId);
        fish = null;
        return false;
    }

    private void CleanupRemotePlayers()
    {
        List<long> invalidIds = null;

        foreach (KeyValuePair<long, FishMove> pair
            in remotePlayerFish)
        {
            if (pair.Value == null ||
                !pair.Value.gameObject.activeInHierarchy ||
                !pair.Value.IsRemoteMqttPlayer)
            {
                if (invalidIds == null)
                {
                    invalidIds = new List<long>();
                }

                invalidIds.Add(pair.Key);
            }
        }

        if (invalidIds == null)
        {
            return;
        }

        for (int i = 0; i < invalidIds.Count; i++)
        {
            remotePlayerFish.Remove(invalidIds[i]);
        }
    }

    private void SendJoinAck(
        long playerId,
        bool success,
        string message)
    {
        if (MyMqtt.Instance == null)
        {
            Debug.LogWarning(
                "[FishGameManager] 无法发送 Join ACK：MyMqtt.Instance 为空。"
            );
            return;
        }

        MyMqtt.Instance.SendFishJoinAck(
            playerId,
            success,
            message
        );
    }

    /// <summary>
    /// 两条鱼发生碰撞后，由 FishMove 调用。
    /// </summary>
    public void TryStartEating(
        FishMove fishA,
        FishMove fishB)
    {
        if (fishA == null || fishB == null ||
            fishA == fishB)
        {
            return;
        }

        if (!fishA.gameObject.activeInHierarchy ||
            !fishB.gameObject.activeInHierarchy)
        {
            return;
        }

        if (fishA.IsBusy ||
            fishB.IsBusy ||
            fishA.IsBeingEaten ||
            fishB.IsBeingEaten)
        {
            return;
        }

        // AI 鱼之间永远不能相互吃。
        // 碰撞双方中必须且只能有一条玩家鱼。
        FishMove player;
        FishMove aiFish;

        if (fishA.IsPlayer && !fishB.IsPlayer)
        {
            player = fishA;
            aiFish = fishB;
        }
        else if (fishB.IsPlayer && !fishA.IsPlayer)
        {
            player = fishB;
            aiFish = fishA;
        }
        else
        {
            return;
        }

        if (player.CanEat(aiFish))
        {
            player.BeginEat(aiFish);
        }
    }

    /// <summary>
    /// 玩家吃掉 AI 鱼后完成计数和升级。
    /// </summary>
    public void CompleteEat(
        FishMove eater,
        FishMove victim)
    {
        bool victimWasDespawned = false;

        if (victim != null &&
            victim.gameObject.activeInHierarchy)
        {
            // 必须在回收鱼之前记录并转换位置，否则对象进入池后位置可能被重置。
            PlayFishDisappearEffect(
                victim.transform.position
            );

            LeanPool.Despawn(victim.gameObject);
            victimWasDespawned = true;
        }

        // 被吃掉一条鱼后，安排补充一条新鱼。
        if (victimWasDespawned &&
            autoRespawnFish)
        {
            RequestRespawnFish();
        }

        if (eater == null ||
            !eater.gameObject.activeInHierarchy)
        {
            return;
        }

        eater.AddEatCount();

        if (eater.HasReachedUpgradeCondition())
        {
            UpgradeFish(eater);
        }
        else
        {
            eater.FinishEatWithoutUpgrade();
        }
    }

    /// <summary>
    /// 在鱼当前的屏幕位置生成一个世界空间特效。
    /// 鱼位于 Screen Space - Camera Canvas 中，因此先转屏幕坐标，
    /// 再投射到指定的世界 Z 平面。
    /// </summary>
    private void PlayFishDisappearEffect(
        Vector3 fishUiWorldPosition)
    {
        if (fishDisappearEffectPrefab == null)
        {
            return;
        }

        Camera cameraToUse = GetWorldEffectCamera();

        if (cameraToUse == null)
        {
            Debug.LogWarning(
                "[FishGameManager] 无法播放鱼消失特效：没有找到可用相机。",
                gameObject
            );
            return;
        }

        Vector2 screenPosition =
            RectTransformUtility.WorldToScreenPoint(
                cameraToUse,
                fishUiWorldPosition
            );

        Ray screenRay =
            cameraToUse.ScreenPointToRay(
                screenPosition
            );

        Plane effectPlane =
            new Plane(
                Vector3.forward,
                new Vector3(0f, 0f, worldEffectZ)
            );

        float enterDistance;
        Vector3 effectWorldPosition;

        if (effectPlane.Raycast(
            screenRay,
            out enterDistance))
        {
            effectWorldPosition =
                screenRay.GetPoint(enterDistance);
        }
        else
        {
            // 标准 2D 正交相机通常不会进入这里，保留回退值避免特效丢失。
            effectWorldPosition =
                new Vector3(
                    fishUiWorldPosition.x,
                    fishUiWorldPosition.y,
                    worldEffectZ
                );
        }

        GameObject effectObject = Instantiate(
            fishDisappearEffectPrefab,
            effectWorldPosition,
            Quaternion.identity,
            worldEffectRoot
        );

        if (effectObject == null)
        {
            return;
        }

        Destroy(
            effectObject,
            Mathf.Max(
                0.05f,
                fishDisappearEffectLifeTime
            )
        );
    }


    /// <summary>
    /// 在鱼升级位置播放升级特效。
    /// 坐标转换逻辑与吃鱼消失特效保持一致。
    /// </summary>
    private void PlayFishUpgradeEffect(Vector3 fishUiWorldPosition)
    {
        if (fishUpgradeEffectPrefab == null)
        {
            return;
        }

        Camera cameraToUse = GetWorldEffectCamera();

        if (cameraToUse == null)
        {
            return;
        }

        Vector2 screenPosition =
            RectTransformUtility.WorldToScreenPoint(
                cameraToUse,
                fishUiWorldPosition
            );

        Ray screenRay =
            cameraToUse.ScreenPointToRay(screenPosition);

        Plane effectPlane =
            new Plane(
                Vector3.forward,
                new Vector3(0f, 0f, worldEffectZ)
            );

        Vector3 effectWorldPosition;

        if (effectPlane.Raycast(screenRay, out float enterDistance))
        {
            effectWorldPosition =
                screenRay.GetPoint(enterDistance);
        }
        else
        {
            effectWorldPosition =
                new Vector3(
                    fishUiWorldPosition.x,
                    fishUiWorldPosition.y,
                    worldEffectZ
                );
        }

        GameObject effectObject = Instantiate(
            fishUpgradeEffectPrefab,
            effectWorldPosition,
            Quaternion.identity,
            worldEffectRoot
        );

        if (effectObject != null)
        {
            Destroy(
                effectObject,
                Mathf.Max(
                    0.05f,
                    fishDisappearEffectLifeTime
                )
            );
        }
    }

    /// <summary>
    /// 按配置的时间间隔持续生成随机水泡。
    /// </summary>
    private IEnumerator BubbleSpawnCoroutine()
    {
        while (true)
        {
            float minimumInterval =
                Mathf.Max(
                    0.05f,
                    Mathf.Min(
                        bubbleSpawnIntervalMin,
                        bubbleSpawnIntervalMax
                    )
                );

            float maximumInterval =
                Mathf.Max(
                    minimumInterval,
                    Mathf.Max(
                        bubbleSpawnIntervalMin,
                        bubbleSpawnIntervalMax
                    )
                );

            yield return new WaitForSeconds(
                Random.Range(
                    minimumInterval,
                    maximumInterval
                )
            );

            SpawnRandomBubbleEffect();
        }
    }

    /// <summary>
    /// 随机选择一个水泡预制体，在当前相机可见范围内生成。
    /// 只修改生成对象的世界坐标，不修改预制体的旋转和缩放。
    /// </summary>
    private void SpawnRandomBubbleEffect()
    {
        GameObject bubblePrefab =
            GetRandomPrefab(bubbleEffectPrefabs);

        if (bubblePrefab == null)
        {
            return;
        }

        Camera cameraToUse = GetWorldEffectCamera();

        if (cameraToUse == null)
        {
            return;
        }

        Vector3 bubbleWorldPosition;

        if (!TryGetRandomWorldPosition(
            cameraToUse,
            out bubbleWorldPosition))
        {
            return;
        }

        Transform parent =
            bubbleEffectRoot != null
                ? bubbleEffectRoot
                : worldEffectRoot;

        GameObject bubbleObject;

        if (parent != null)
        {
            // 使用只传 Parent 的 Instantiate 重载，
            // 保留水泡预制体原本的 localRotation 和 localScale。
            bubbleObject = Instantiate(
                bubblePrefab,
                parent
            );
        }
        else
        {
            bubbleObject = Instantiate(
                bubblePrefab
            );
        }

        if (bubbleObject == null)
        {
            return;
        }

        // 只设置世界坐标，不设置 rotation，
        // 因此不会覆盖水泡预制体原本的角度。
        bubbleObject.transform.position =
            bubbleWorldPosition;

        Destroy(
            bubbleObject,
            Mathf.Max(
                0.05f,
                bubbleEffectLifeTime
            )
        );
    }

    /// <summary>
    /// 从相机视口下方区域随机选择一点，并投射到 World Effect Z 平面。
    /// </summary>
    private bool TryGetRandomWorldPosition(
        Camera cameraToUse,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;

        if (cameraToUse == null)
        {
            return false;
        }

        float padding =
            Mathf.Clamp(
                bubbleViewportPadding,
                0f,
                0.49f
            );

        float maximumViewportY =
            Mathf.Clamp(
                bubbleSpawnHeightRatio,
                padding,
                1f - padding
            );

        Vector3 viewportPosition =
            new Vector3(
                Random.Range(
                    padding,
                    1f - padding
                ),
                Random.Range(
                    padding,
                    maximumViewportY
                ),
                0f
            );

        Ray viewportRay =
            cameraToUse.ViewportPointToRay(
                viewportPosition
            );

        Plane bubblePlane =
            new Plane(
                Vector3.forward,
                new Vector3(
                    0f,
                    0f,
                    worldEffectZ
                )
            );

        float enterDistance;

        if (!bubblePlane.Raycast(
            viewportRay,
            out enterDistance))
        {
            return false;
        }

        worldPosition =
            viewportRay.GetPoint(
                enterDistance
            );

        return true;
    }

    /// <summary>
    /// 获取 UI 坐标与世界坐标转换时使用的相机。
    /// </summary>
    private Camera GetWorldEffectCamera()
    {
        if (worldEffectCamera != null)
        {
            return worldEffectCamera;
        }

        Canvas canvas = null;

        if (fishContainer != null)
        {
            canvas =
                fishContainer.GetComponentInParent<Canvas>();
        }

        if (canvas != null &&
            canvas.worldCamera != null)
        {
            return canvas.worldCamera;
        }

        return Camera.main;
    }

    /// <summary>
    /// 请求补充一条鱼。
    /// 使用协程延迟生成，避免小鱼刚消失就立刻在画面中出现。
    /// </summary>
    private void RequestRespawnFish()
    {
        if (!autoRespawnFish)
        {
            return;
        }

        if (pendingRespawnCount >= maxPendingRespawns)
        {
            return;
        }

        pendingRespawnCount++;

        StartCoroutine(
            RespawnFishCoroutine()
        );
    }

    private IEnumerator RespawnFishCoroutine()
    {
        if (respawnDelay > 0f)
        {
            yield return new WaitForSeconds(
                respawnDelay
            );
        }

        pendingRespawnCount =
            Mathf.Max(
                0,
                pendingRespawnCount - 1
            );

        SpawnReplacementFish();
    }

    /// <summary>
    /// 生成一条用于补充数量的新鱼。
    /// </summary>
    private void SpawnReplacementFish()
    {
        GameObject prefab =
            GetRespawnPrefab();

        if (prefab == null)
        {
            Debug.LogWarning(
                "[FishGameManager] 没有可用于补充生成的鱼 Prefab。"
            );

            return;
        }

        FishMove newFish = SpawnFish(prefab, GetEdgeSpawnPosition());

        if (newFish != null)
        {
            Vector2 direction =
                newFish.transform
                    .GetComponent<RectTransform>()
                    .anchoredPosition.x < 0f
                        ? Vector2.right
                        : Vector2.left;

            newFish.SetMoveDirection(
                direction
            );
        }
    }

    /// <summary>
    /// 补鱼同样按照玩家当前等级动态决定：
    /// 一半概率低于玩家，其余以同级鱼为主。
    /// </summary>
    private GameObject GetRespawnPrefab()
    {
        if (allFish.Count == 0)
        {
            return null;
        }

        int playerPowerIndex =
            GetCurrentPlayerPowerIndex();

        bool canSpawnLower =
            playerPowerIndex > 0;

        if (canSpawnLower &&
            Random.value <
            Mathf.Clamp01(
                lowerThanPlayerSpawnRatio
            ))
        {
            GameObject lowerPrefab =
                GetRandomPrefabByPowerIndexRange(
                    0,
                    playerPowerIndex - 1
                );

            if (lowerPrefab != null)
            {
                return lowerPrefab;
            }
        }

        GameObject normalPrefab =
            GetSameOrSlightlyHigherPrefab(
                playerPowerIndex
            );

        return normalPrefab != null
            ? normalPrefab
            : GetAnyAvailableFishPrefab();
    }

    /// <summary>
    /// 获取玩家初始鱼。等级从 1 开始，等级 2 对应完整成长列表下标 1。
    /// </summary>
    private GameObject GetInitialPlayerPrefab()
    {
        if (allFish.Count > 0)
        {
            int requestedIndex =
                Mathf.Max(
                    0,
                    initialPlayerLevel - 1
                );

            int actualIndex =
                Mathf.Clamp(
                    requestedIndex,
                    0,
                    allFish.Count - 1
                );

            if (requestedIndex != actualIndex)
            {
                Debug.LogWarning(
                    "[FishGameManager] 玩家初始等级超出鱼列表范围，" +
                    "已自动使用当前可用的最高等级：" +
                    (actualIndex + 1)
                );
            }

            return allFish[actualIndex].prefab;
        }

        return initialPlayerFishPrefab;
    }

    /// <summary>
    /// 获取玩家当前在完整成长列表中的强度下标。
    /// </summary>
    private int GetCurrentPlayerPowerIndex()
    {
        if (PlayerFish != null &&
            PlayerFish.gameObject.activeInHierarchy)
        {
            return Mathf.Clamp(
                PlayerFish.PowerIndex,
                0,
                Mathf.Max(0, allFish.Count - 1)
            );
        }

        return Mathf.Clamp(
            initialPlayerLevel - 1,
            0,
            Mathf.Max(0, allFish.Count - 1)
        );
    }

    /// <summary>
    /// 获取与玩家同级或只高少量等级的鱼。
    /// 默认情况下，高于玩家的鱼只占剩余鱼群的 20%。
    /// </summary>
    private GameObject GetSameOrSlightlyHigherPrefab(
        int playerPowerIndex)
    {
        if (allFish.Count == 0)
        {
            return null;
        }

        playerPowerIndex =
            Mathf.Clamp(
                playerPowerIndex,
                0,
                allFish.Count - 1
            );

        int maxHigherIndex =
            Mathf.Min(
                allFish.Count - 1,
                playerPowerIndex +
                Mathf.Max(
                    0,
                    maxSpawnLevelAbovePlayer
                )
            );

        bool canSpawnHigher =
            maxHigherIndex >
            playerPowerIndex;

        if (canSpawnHigher &&
            Random.value <
            Mathf.Clamp01(
                higherThanPlayerChance
            ))
        {
            return GetRandomPrefabByPowerIndexRange(
                playerPowerIndex + 1,
                maxHigherIndex
            );
        }

        return allFish[playerPowerIndex].prefab;
    }

    /// <summary>
    /// 从完整成长列表的指定强度范围内随机选择一条鱼。
    /// </summary>
    private GameObject GetRandomPrefabByPowerIndexRange(
        int minPowerIndex,
        int maxPowerIndex)
    {
        if (allFish.Count == 0)
        {
            return null;
        }

        int minIndex =
            Mathf.Clamp(
                minPowerIndex,
                0,
                allFish.Count - 1
            );

        int maxIndex =
            Mathf.Clamp(
                maxPowerIndex,
                0,
                allFish.Count - 1
            );

        if (minIndex > maxIndex)
        {
            return null;
        }

        int selectedIndex =
            Random.Range(
                minIndex,
                maxIndex + 1
            );

        return allFish[selectedIndex].prefab;
    }

    private GameObject GetRandomPrefab(
        List<GameObject> prefabList)
    {
        if (prefabList == null ||
            prefabList.Count == 0)
        {
            return null;
        }

        // 避免列表中存在空引用。
        int validCount = 0;

        for (int i = 0;
             i < prefabList.Count;
             i++)
        {
            if (prefabList[i] != null)
            {
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return null;
        }

        int selectedIndex =
            Random.Range(
                0,
                validCount
            );

        for (int i = 0;
             i < prefabList.Count;
             i++)
        {
            GameObject prefab =
                prefabList[i];

            if (prefab == null)
            {
                continue;
            }

            if (selectedIndex == 0)
            {
                return prefab;
            }

            selectedIndex--;
        }

        return null;
    }

    private GameObject GetAnyAvailableFishPrefab()
    {
        return GetRandomPrefab(FishList);
    }

    private Vector2 GetEdgeSpawnPosition()
    {
        if (moveArea == null)
        {
            return Vector2.zero;
        }

        Rect rect = moveArea.rect;

        bool spawnFromLeft =
            Random.value < 0.5f;

        float x =
            spawnFromLeft
                ? rect.xMin + spawnPadding
                : rect.xMax - spawnPadding;

        float y =
            Random.Range(
                rect.yMin + spawnPadding,
                rect.yMax - spawnPadding
            );

        return new Vector2(x, y);
    }

    /// <summary>
    /// 升级成完整成长列表中的下一种鱼。
    /// </summary>
    private void UpgradeFish(FishMove oldFish)
    {
        int nextIndex = oldFish.PowerIndex + 1;

        // 已经是最终鱼，无法继续升级。
        if (nextIndex >= allFish.Count)
        {
            oldFish.ResetEatCount();
            oldFish.FinishEatWithoutUpgrade();
            return;
        }

        FishPrefabData nextFishData =
            allFish[nextIndex];

        RectTransform oldRect =
            oldFish.transform as RectTransform;

        Transform parent = oldFish.transform.parent;
        Vector3 oldPosition = oldFish.transform.localPosition;
        Quaternion oldRotation =
            oldFish.transform.localRotation;
        Vector2 oldAnchoredPosition =
            oldRect != null
                ? oldRect.anchoredPosition
                : new Vector2(oldPosition.x, oldPosition.y);

        Vector2 oldDirection =
            oldFish.MoveDirection;
        float oldMoveStrength =
            oldFish.MoveStrength;

        FishControlMode oldControlMode =
            oldFish.ControlMode;
        long oldPlayerId = oldFish.PlayerId;
        string oldPlayerNickName =
            oldFish.PlayerNickName;
        bool wasPlayer = oldFish.IsPlayer;
        bool wasBoosting = oldFish.IsBoosting;
        float remainingSpeedBoostTime =
            oldFish.SpeedBoostRemainingTime;

        // 先生成新鱼，再回收旧鱼。
        GameObject newFishObject = LeanPool.Spawn(
            nextFishData.prefab,
            parent,
            false
        );

        if (newFishObject == null)
        {
            oldFish.FinishEatWithoutUpgrade();
            return;
        }

        RectTransform newRect =
            newFishObject.GetComponent<RectTransform>();

        if (newRect != null)
        {
            // 不使用 Vector2 anchoredPosition，必须显式把 Z 清零。
            // 不复制旧鱼的 localScale，保留新等级 Prefab 自己的尺寸。
            newRect.anchoredPosition3D =
                new Vector3(
                    oldAnchoredPosition.x,
                    oldAnchoredPosition.y,
                    0f
                );

            newRect.localRotation =
                oldRotation;
        }
        else
        {
            newFishObject.transform.localPosition =
                new Vector3(
                    oldPosition.x,
                    oldPosition.y,
                    0f
                );

            newFishObject.transform.localRotation =
                oldRotation;
        }

        FishMove newFish =
            newFishObject.GetComponent<FishMove>();

        if (newFish == null)
        {
            LeanPool.Despawn(newFishObject);
            oldFish.FinishEatWithoutUpgrade();
            return;
        }

        newFish.InitializeFish(
            this,
            nextFishData.globalPowerIndex,
            GetUpgradeEatCount(nextFishData.globalPowerIndex),
            oldControlMode,
            oldPlayerId,
            oldPlayerNickName
        );

        newFish.SetMoveInput(
            oldDirection,
            oldMoveStrength
        );
        newFish.ResetEatCount();
        newFish.PlayIdleAnimation();
        newFish.ResumeMovement();

        if (wasPlayer)
        {
            // 玩家鱼放到当前鱼群 UI 的最上层，避免被后生成的 UI 元素覆盖。
            newFish.transform.SetAsLastSibling();

            if (oldControlMode ==
                FishControlMode.LocalKeyboard)
            {
                PlayerFish = newFish;

                if (remainingSpeedBoostTime > 0f)
                {
                    newFish.StartSpeedBoost(
                        remainingSpeedBoostTime
                    );
                }
            }
            else if (oldControlMode ==
                     FishControlMode.RemoteMqtt &&
                     oldPlayerId > 0L)
            {
                remotePlayerFish[oldPlayerId] = newFish;

                if (wasBoosting)
                {
                    newFish.SetExternalBoostPressed(true);
                }
            }

            PlayFishUpgradeEffect(oldFish.transform.position);
        }

        if (playEffectWhenUpgrade)
        {
            PlayFishDisappearEffect(
                oldFish.transform.position
            );
        }

        LeanPool.Despawn(oldFish.gameObject);

        // 对象池复用 SkeletonGraphic 时，强制刷新一次 Canvas 网格。
        // 这不是位置修正，而是避免升级同一帧内 CanvasRenderer 保留旧的脏状态。
        newFish.RefreshUiGraphic();
        Canvas.ForceUpdateCanvases();
    }

    private int FindPrefabProgressionIndex(
        GameObject prefab)
    {
        for (int i = 0; i < allFish.Count; i++)
        {
            if (allFish[i].prefab == prefab)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 根据鱼当前等级计算升级所需吃鱼数量。
    /// PowerIndex 从 0 开始，因此等级 1 使用基础数量，
    /// 后续每升一级增加 additionalEatCountPerLevel。
    /// </summary>
    public int GetUpgradeEatCount(int powerIndex)
    {
        int safePowerIndex = Mathf.Max(0, powerIndex);

        int requiredCount =
            Mathf.Max(1, baseUpgradeEatCount) +
            safePowerIndex *
            Mathf.Max(0, additionalEatCountPerLevel);

        if (maxUpgradeEatCount > 0)
        {
            requiredCount =
                Mathf.Min(
                    requiredCount,
                    maxUpgradeEatCount
                );
        }

        return Mathf.Max(1, requiredCount);
    }

    /// <summary>
    /// 获取 MQTT 玩家进场生成位置。
    /// 固定创建在鱼活动区域正中心，不使用左右边缘或随机坐标。
    /// </summary>
    public Vector2 GetMqttPlayerSpawnPosition()
    {
        if (moveArea == null)
        {
            return Vector2.zero;
        }

        Rect rect = moveArea.rect;

        return new Vector2(
            (rect.xMin + rect.xMax) * 0.5f,
            (rect.yMin + rect.yMax) * 0.5f
        );
    }

    /// <summary>
    /// 获取 UI 区域内随机坐标。
    /// </summary>
    public Vector2 GetRandomSpawnPosition()
    {
        if (moveArea == null)
        {
            return Vector2.zero;
        }

        Rect rect = moveArea.rect;

        float minX =
            rect.xMin + spawnPadding;
        float maxX =
            rect.xMax - spawnPadding;
        float minY =
            rect.yMin + spawnPadding;
        float maxY =
            rect.yMax - spawnPadding;

        if (minX > maxX)
        {
            minX = maxX =
                (rect.xMin + rect.xMax) * 0.5f;
        }

        if (minY > maxY)
        {
            minY = maxY =
                (rect.yMin + rect.yMax) * 0.5f;
        }

        return new Vector2(
            Random.Range(minX, maxX),
            Random.Range(minY, maxY)
        );
    }

    public Rect GetMovementRect()
    {
        if (moveArea == null)
        {
            return new Rect(
                -960f,
                -540f,
                1920f,
                1080f
            );
        }

        return moveArea.rect;
    }

    private void OnDestroy()
    {
        MyMqtt.MainThreadMessageReceived -=
            OnMqttMainThreadMessage;

        remotePlayerFish.Clear();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private class FishPrefabData
    {
        public GameObject prefab;
        public int globalPowerIndex;
    }
}
