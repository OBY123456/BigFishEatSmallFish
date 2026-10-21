using UnityEngine;
using Lean.Pool;

public class FishCore : MonoBehaviour
{
    [Header("鱼的当前等级，游戏最高等级为10")]
    public int level = 1;
    [Header("玩家当前经验，升级后清零")]
    public int score = 0;
    [Header("玩家昵称，NPC可使用NPC作为名称")]
    public string playerName = "玩家";
    [Header("是否玩家鱼；普通NPC和特殊鱼均不勾选")]
    public bool isPlayer = false;
    [Header("远程玩家是否已收到本次出生后的第一条MQTT方向或加速消息")]
    [HideInInspector] public bool hasReceivedInitialMqttControl = false;
    [Header("单条鱼的基础游动速度，世界单位/秒")]
    public float baseSpeed = 5f;

    public static readonly int[] LevelThresholds = { 0, 10, 15, 25, 35, 45, 135, 175, 220, 270 };
    [Header("已累计获得的成长值")]
    public int currentGrowth = 0;

    [Header("该鱼是否处于死亡或已回收状态")]
    public bool isDead = false;
    [Header("玩家当前是否无敌，无法被特殊鱼吃掉")]
    public bool isInvincible = false;
    [Header("猎食令是否生效：可以吞噬等级比自己低的其他玩家")]
    public bool canEatHigher = false;
    [Header("尖刺护盾是否生效：无敌并让接触到的玩家扣20分")]
    public bool isShielded = false;
    [Header("是否处于隐身状态，鲨鱼会忽略")]
    public bool isInvisible = false;
    [Header("隐身时鱼的剩余不透明度，1完全不透明，默认0.75")]
    [Range(0.1f, 1f)] public float invisibilityOpacity = 0.75f;
    [Header("特殊鱼加速及限速等Buff叠加倍率")]
    public float speedMultiplier = 1f;
    [Header("玩家按加速键时使用的额外倍率")]
    public float boostMultiplier = 1.8f;
    [Header("当前是否正在加速状态")]
    public bool isBoosting = false;
    [HideInInspector] public long playerId;
    [HideInInspector] public int specialFishType = -1;
    [HideInInspector] public int npcPrefabSlot = -1;
    private int buffVersion;
    private int slowVersion;
    private int invisibleVersion;
    private int shieldVersion;
    private bool generalInvulnerable;
    private float temporarySpeedBoost = 1f;
    private float temporarySlowFactor = 1f;
    private float areaSlowFactor = 1f;
    private float areaSlowUntil;
    private Transform itemVortexTarget;
    private float itemVortexUntil;
    private readonly System.Collections.Generic.HashSet<int> shieldHitPlayers =
        new System.Collections.Generic.HashSet<int>();
    private Coroutine specialLifetimeRoutine;
    private SpriteRenderer[] invisibilityRenderers;
    private float[] invisibleOriginalAlphas;
    private GameObject upgradeEffectInstance;
    private Coroutine upgradeEffectRoutine;

    [Header("是否已经被场景漩涡风暴锁定（仅运行时调试显示）")]
    [HideInInspector] public bool isCapturedByVortex;
    private int capturedVortexId = -1;
    private float vortexEatRestoreTime;

    [HideInInspector] public float currentSpeed;

    private Rigidbody2D rb;
    private IMovement movementComponent;
    private FishVisuals visuals;
    private float originalNpcAnimationSpeed = 1f;
    private bool hasCachedNpcAnimationSpeed;
    private Vector3 lastEatTargetWorldPosition;
    private bool hasPendingEatPunchOnUpgrade;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        movementComponent = GetComponent<IMovement>();
        visuals = GetComponent<FishVisuals>();
        if (visuals != null && visuals.animator != null)
        {
            originalNpcAnimationSpeed = visuals.animator.speed;
            hasCachedNpcAnimationSpeed = true;
        }

        if (FishManager.Instance != null)
            baseSpeed = FishManager.Instance.globalBaseSpeed;

        RefreshLevelSpeed();
    }

    /// <summary>
    /// 等级基础速度 = globalBaseSpeed * (1 + (level - 1) * levelSpeedIncreasePerLevel)。
    /// 加速、闪电鱼Buff、时间缓速等在 FixedUpdate 内再乘一次各自倍率。
    /// </summary>
    public void RefreshLevelSpeed()
    {
        FishManager manager = FishManager.Instance;
        if (manager != null) baseSpeed = Mathf.Max(0f, manager.globalBaseSpeed);
        float step = manager != null ? Mathf.Max(0f, manager.levelSpeedIncreasePerLevel) : 0.1f;
        currentSpeed = Mathf.Max(0f, baseSpeed) *
            (1f + Mathf.Max(0, Mathf.Clamp(level, 1, 10) - 1) * step);
    }

    void FixedUpdate()
    {
        if (isDead) return;

        // 支持运行时调整 globalBaseSpeed、等级或每级增长比例。
        RefreshLevelSpeed();

        float eventSpeedMultiplier = !isPlayer && FishManager.Instance != null
            ? FishManager.Instance.NonPlayerEventSpeedMultiplier : 1f;
        // 疯狂海域不仅加快位移，也同步加快NPC/特殊鱼的游泳动画；事件结束立刻还原。
        if (!isPlayer && visuals != null)
        {
            // FishCore.Awake 可能早于 FishVisuals.Awake，首次用到 Animator 时再补记原始速度。
            if (!hasCachedNpcAnimationSpeed && visuals.animator != null)
            {
                originalNpcAnimationSpeed = visuals.animator.speed;
                hasCachedNpcAnimationSpeed = true;
            }
            visuals.SetAnimationSpeed(originalNpcAnimationSpeed * eventSpeedMultiplier);
        }

        if (rb != null && TickVortexCapture()) return;
        if (rb != null && TickItemVortexPull()) return;

        float activeAreaSlow = Time.time < areaSlowUntil ? areaSlowFactor : 1f;
        float finalSpeed = currentSpeed * speedMultiplier * temporarySpeedBoost *
            temporarySlowFactor * activeAreaSlow * (isBoosting ? boostMultiplier : 1f) * eventSpeedMultiplier;

        // 防崩处理：如果组件或刚体丢失，直接跳过
        if (movementComponent != null && movementComponent.IsActive && rb != null)
        {
            movementComponent.Tick(rb, finalSpeed);
        }
    }

    private void OnDisable()
    {
        ResetVortexState();
        // 禁用父鱼时不允许在这里立刻 LeanPool.Despawn 子特效；
        // LeanPool 回收会 SetParent，引发 Unity 的父物体正在禁用的错误。
        ClearUpgradeEffect(true);
        if (FishManager.Instance != null)
            FishManager.Instance.QueueEquippedVorticesForSafeDespawn(this);
        ResetGameplayEffects();
    }

    public void ResetGameplayEffects()
    {
        invincibleVersion++;
        shieldVersion++;
        huntingVersion++;
        invisibleVersion++;
        buffVersion++;
        slowVersion++;
        generalInvulnerable = false;
        hasReceivedInitialMqttControl = playerId <= 0L;
        isShielded = false;
        canEatHigher = false;
        temporarySpeedBoost = 1f;
        temporarySlowFactor = 1f;
        areaSlowFactor = 1f;
        areaSlowUntil = 0f;
        itemVortexTarget = null;
        itemVortexUntil = 0f;
        shieldHitPlayers.Clear();
        hasPendingEatPunchOnUpgrade = false;
        RestoreInvisibilityVisuals();
        RefreshInvulnerability();
    }

    private void RefreshInvulnerability()
    {
        isInvincible = generalInvulnerable || isShielded || isInvisible;
    }

    public void ResetVortexState()
    {
        capturedVortexId = -1;
        isCapturedByVortex = false;
        vortexEatRestoreTime = 0f;
    }

    private void ReleaseVortex(FishManager manager)
    {
        if (capturedVortexId < 0) return;

        capturedVortexId = -1;
        isCapturedByVortex = false;
        float delay = manager != null ? manager.vortexReleaseEatDelay : 2f;
        vortexEatRestoreTime = Mathf.Max(vortexEatRestoreTime, Time.time + Mathf.Max(0f, delay));
    }

    public bool IsVortexEatingBlocked()
    {
        if (isDead) return false;

        FishManager manager = FishManager.Instance;
        if (capturedVortexId > 0)
        {
            Vector2 center;
            if (manager == null || !manager.TryGetVortexPosition(capturedVortexId, out center))
                ReleaseVortex(manager);
            else
                return true;
        }

        if (Time.time < vortexEatRestoreTime) return true;

        // 尚未进入 FixedUpdate 的新碰撞也要检查，否则第一次撞进漩涡的一帧可能发生吞噬。
        if (manager != null)
        {
            int vortexId;
            Vector2 center;
            if (manager.TryGetVortexCaptureTarget(this, transform.position, out vortexId, out center))
                return true;
        }

        return false;
    }

    private bool TickVortexCapture()
    {
        FishManager manager = FishManager.Instance;
        if (manager == null)
        {
            ReleaseVortex(null);
            return false;
        }

        Vector2 center;
        if (capturedVortexId > 0)
        {
            if (!manager.TryGetVortexPosition(capturedVortexId, out center))
            {
                // 漩涡事件销毁，立刻恢复移动，但继续禁止吞噬指定秒数。
                ReleaseVortex(manager);
                return false;
            }
        }
        else
        {
            int selectedId;
            if (!manager.TryGetVortexCaptureTarget(this, rb.position, out selectedId, out center))
                return false;

            // 进入漩涡范围的第一帧就锁定这个漩涡，不允许自行游出边界。
            capturedVortexId = selectedId;
            isCapturedByVortex = true;
        }

        float snapRadius = Mathf.Max(0.02f, manager.vortexCaptureRadius);
        Vector2 nextPosition;
        if ((center - rb.position).sqrMagnitude <= snapRadius * snapRadius)
        {
            // 到达中心后完全跟随移动的漩涡，摇杆、AI 和加速均无法挣脱。
            nextPosition = center;
        }
        else
        {
            // 吸附速度至少略高于漩涡自身速度，避免中心移动太快导致鱼永远追不上。
            float speed = Mathf.Max(manager.vortexPullSpeed, manager.vortexMoveSpeed + 0.1f);
            nextPosition = Vector2.MoveTowards(rb.position, center, speed * Time.fixedDeltaTime);
        }

        rb.MovePosition(manager.ClampPosition(nextPosition));
        return true;
    }

    public void ApplyItemVortexPull(Transform center, float remainingDuration)
    {
        if (center == null || remainingDuration <= 0f || isDead) return;
        itemVortexTarget = center;
        itemVortexUntil = Time.time + remainingDuration;
    }

    private bool TickItemVortexPull()
    {
        if (itemVortexTarget == null || !itemVortexTarget.gameObject.activeInHierarchy || Time.time >= itemVortexUntil)
        {
            itemVortexTarget = null;
            return false;
        }

        float speed = FishManager.Instance != null ? FishManager.Instance.itemVortexPullSpeed : 4f;
        Vector2 newPosition = Vector2.MoveTowards(rb.position, (Vector2)itemVortexTarget.position,
            Mathf.Max(0f, speed) * Time.fixedDeltaTime);
        if (FishManager.Instance != null) newPosition = FishManager.Instance.ClampPosition(newPosition);
        rb.MovePosition(newPosition);
        return true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ProcessTriggerCollision(other, true);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // 漩涡结束两秒后，如果两条鱼仍然重叠，也能恢复吃/被吃，不必先游开再重新撞上。
        ProcessTriggerCollision(other, false);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        FishCore fish = other != null ? other.GetComponentInParent<FishCore>() : null;
        if (fish != null) shieldHitPlayers.Remove(fish.GetInstanceID());
    }

    private bool RequiresInitialMqttControl()
    {
        return isPlayer && playerId > 0L && !hasReceivedInitialMqttControl;
    }

    public void MarkInitialMqttControlReceived()
    {
        if (isPlayer && playerId > 0L) hasReceivedInitialMqttControl = true;
    }

    private void ProcessTriggerCollision(Collider2D other, bool isNewCollision)
    {
        if (isDead || other == null) return;

        if (isNewCollision && isPlayer)
        {
            ItemBase item = other.GetComponentInParent<ItemBase>();
            if (item != null)
            {
                item.UseItem(this);
                return;
            }
        }

        FishCore otherFish = other.GetComponentInParent<FishCore>();
        if (otherFish == null || otherFish == this || otherFish.isDead) return;
        if (RequiresInitialMqttControl() || otherFish.RequiresInitialMqttControl()) return;
        if (IsVortexEatingBlocked() || otherFish.IsVortexEatingBlocked()) return;

        if (isPlayer && otherFish.isPlayer)
        {
            // 只有尖刺护盾会扣分；出生无敌与隐身无敌均不会触发扣分。
            if (isShielded && shieldHitPlayers.Add(otherFish.GetInstanceID()))
            {
                if (otherFish.level < 10)
                    otherFish.score = Mathf.Max(0, otherFish.score - 20);
                else
                    otherFish.score = 0;
                otherFish.UpdateVisuals();
            }

            if (canEatHigher && level > otherFish.level && !otherFish.isInvincible)
                EatFish(otherFish);
            return;
        }

        if (isPlayer && !otherFish.isPlayer)
        {
            bool canEat = level >= otherFish.level;
            if (canEat) EatFish(otherFish);
            else if (otherFish.specialFishType >= 0 && otherFish.level > level &&
                !isInvincible && !(otherFish.specialFishType == 3 && isInvisible))
                GetEatenBy(otherFish);
        }
        else if (!isPlayer && otherFish.isPlayer && level > otherFish.level)
        {
            // 普通NPC不能吃玩家；特殊鱼必须等级高于玩家且玩家不处于无敌状态。
            if (specialFishType < 0) return;
            if (specialFishType == 3 && otherFish.isInvisible) return;
            if (!otherFish.isInvincible) otherFish.GetEatenBy(this);
        }
    }

    private void EatFish(FishCore target)
    {
        if (target == null || target.isDead || IsVortexEatingBlocked() || target.IsVortexEatingBlocked()) return;

        // 在猎物被 LeanPool 回收之前保存世界位置，立即让鱼头朝向猎物，再播放吃鱼动画。
        lastEatTargetWorldPosition = target.transform.position;
        hasPendingEatPunchOnUpgrade = true;
        if (visuals != null) visuals.PlayEatPunchTowards(lastEatTargetWorldPosition);

        // score 直接作为当前经验值；升级后清零，不保留溢出经验。
        float multiplier = FishManager.Instance != null ? FishManager.Instance.NpcScoreMultiplier : 1f;
        if (target.isPlayer && canEatHigher) multiplier *= 1.5f;
        score += Mathf.RoundToInt(10f * multiplier);

        ApplySpecialFishBonus(target);
        target.Die();
        bool levelChanged = CheckLevelUp();
        if (levelChanged) return; // 升级生成的新鱼已经继承了吃鱼方向和动画。
        hasPendingEatPunchOnUpgrade = false;
        UpdateVisuals();
    }

    private void ApplySpecialFishBonus(FishCore target)
    {
        if (target == null) return;
        if (target.specialFishType == 0)
        {
            float duration = FishManager.Instance != null ? FishManager.Instance.lightningBuffDuration : 10f;
            ApplySpeedMultiplier(2.5f, duration);
        }
        else if (target.specialFishType == 1)
        {
            // 黄金鱼奖励直接计入当前经验。
            if (level < 10) score += 50;
        }
        else if (target.specialFishType == 2)
        {
            float duration = FishManager.Instance != null ? FishManager.Instance.giantBonusDuration : 10f;
            StartCoroutine(TemporaryLevelBoost(duration));
        }
    }

    private System.Collections.IEnumerator TemporaryLevelBoost(float duration)
    {
        if (level >= 10) yield break;
        level++;
        RefreshLevelSpeed();
        // 临时等级变化只更新当前鱼的文本；永久升级时才通过经验系统替换完整预制体。
        UpdateVisuals();
        if (isPlayer) PlayUpgradeEffect();
        yield return new WaitForSeconds(duration);
        level = Mathf.Max(1, level - 1);
        RefreshLevelSpeed();
        // 临时等级变化只更新当前鱼的文本；永久升级时才通过经验系统替换完整预制体。
        // 临时等级结束后再按真实累计成长值检查永久升级，避免吞噬成长被漏算。
        if (isPlayer)
        {
            bool replaced = CheckLevelUp();
            if (replaced) yield break;
        }
        UpdateVisuals();
    }

    public void StartSpecialLifetime(float duration)
    {
        if (specialLifetimeRoutine != null) StopCoroutine(specialLifetimeRoutine);
        specialLifetimeRoutine = StartCoroutine(SpecialLifetimeRoutine(duration));
    }

    private System.Collections.IEnumerator SpecialLifetimeRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (this != null && gameObject.activeInHierarchy && specialFishType == 2) Die();
    }

    public void GetEatenBy(FishCore predator)
    {
        if (isDead || isInvincible || IsVortexEatingBlocked()) return;
        if (predator != null && predator.IsVortexEatingBlocked()) return;
        Debug.Log("玩家被大鱼吃掉了！");
        Die();
    }

    public void Die()
    {
        if (isDead) return;
        isDead = true;
        if (FishManager.Instance != null)
        {
            FishManager.Instance.NotifyFishDespawned(this);
            FishManager.Instance.SpawnBeEatenEffect(transform.position);
            if (specialFishType == 3) FishManager.Instance.NotifySharkDespawned(this);
            if (isPlayer)
            {
                FishManager.Instance.HandlePlayerDeath(this);
                return;
            }
        }
        LeanPool.Despawn(gameObject);
    }

    private bool CheckLevelUp()
    {
        // 10级封顶：经验不再累计，头顶只显示玩家名字和等级。
        if (level >= 10)
        {
            score = 0;
            currentGrowth = 0;
            UpdateVisuals();
            return false;
        }

        int thresholdIndex = Mathf.Clamp(level, 0, LevelThresholds.Length - 1);
        int requiredExperience = Mathf.Max(1, LevelThresholds[thresholdIndex]);
        if (score < requiredExperience) return false;

        int oldLevel = level;
        level = Mathf.Min(10, level + 1);
        score = 0;
        currentGrowth = 0;
        RefreshLevelSpeed();

        if (isPlayer && FishManager.Instance != null)
        {
            // 必须先保存：替换旧鱼时会触发 OnDisable 并重置对象池状态。
            bool continueEatAnimation = hasPendingEatPunchOnUpgrade;
            Vector3 eatenFishPosition = lastEatTargetWorldPosition;
            FishCore replacement = FishManager.Instance.ReplacePlayerFishForLevelUp(this, level);
            if (replacement != null)
            {
                // 新等级的鱼继续面朝刚吃掉的鱼，播放前冲、放大、回弹。
                if (continueEatAnimation)
                {
                    FishVisuals upgradedVisuals = replacement.GetComponent<FishVisuals>();
                    if (upgradedVisuals != null) upgradedVisuals.PlayEatPunchTowards(eatenFishPosition);
                }
                replacement.PlayUpgradeEffect();
                return true;
            }
        }

        UpdateVisuals();
        if (isPlayer && level > oldLevel) PlayUpgradeEffect();
        return false;
    }

    private void PlayUpgradeEffect()
    {
        if (!isPlayer || isDead || FishManager.Instance == null) return;

        ClearUpgradeEffect();
        upgradeEffectInstance = FishManager.Instance.SpawnUpgradeEffect(transform);
        if (upgradeEffectInstance == null) return;

        upgradeEffectRoutine = StartCoroutine(UpgradeEffectRoutine(FishManager.Instance.upgradeEffectDuration));
    }

    private System.Collections.IEnumerator UpgradeEffectRoutine(float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
        upgradeEffectRoutine = null;

        if (upgradeEffectInstance != null)
        {
            LeanPool.Despawn(upgradeEffectInstance);
            upgradeEffectInstance = null;
        }
    }

    // 主动回收鱼之前调用：父鱼此刻仍处于正常激活状态，允许 LeanPool 修改层级。
    public void PrepareUpgradeEffectForDespawn()
    {
        ClearUpgradeEffect();
    }

    private void ClearUpgradeEffect(bool parentIsDisabling = false)
    {
        if (upgradeEffectRoutine != null)
        {
            StopCoroutine(upgradeEffectRoutine);
            upgradeEffectRoutine = null;
        }

        if (upgradeEffectInstance == null) return;

        GameObject effect = upgradeEffectInstance;
        upgradeEffectInstance = null; // 先清引用，避免重复回收。

        if (parentIsDisabling)
        {
            // OnDisable 内不直接改子物体的父级，交给管理器下一帧安全回收。
            if (FishManager.Instance != null)
                FishManager.Instance.QueueUpgradeEffectForSafeDespawn(effect);
        }
        else
        {
            // 鱼仍处于激活状态的正常结束、刷新特效、升级换鱼路径。
            LeanPool.Despawn(effect);
        }
    }

    public void UpdateVisuals()
    {
        if (visuals != null) visuals.UpdateText(level, score, playerName, playerId, isPlayer);
    }


    public void SetBoostMultiplier(float multiplier)
    {
        boostMultiplier = Mathf.Max(1f, multiplier);
    }

    public void SetBoosting(bool boosting)
    {
        isBoosting = boosting;
        if (visuals != null) visuals.SetAnimationSpeed(isBoosting ? 1.5f : 1f);
    }

    private int invincibleVersion;
    private int huntingVersion;

    public void ApplyInvincibility(float duration)
    {
        invincibleVersion++;
        generalInvulnerable = true;
        RefreshInvulnerability();
        StartCoroutine(InvincibilityRoutine(Mathf.Max(0f, duration), invincibleVersion));
    }

    private System.Collections.IEnumerator InvincibilityRoutine(float duration, int version)
    {
        yield return new WaitForSeconds(duration);
        if (version != invincibleVersion) yield break;
        generalInvulnerable = false;
        RefreshInvulnerability();
    }

    public void ApplyShield(float duration)
    {
        shieldVersion++;
        isShielded = true;
        shieldHitPlayers.Clear();
        RefreshInvulnerability();
        StartCoroutine(ShieldRoutine(Mathf.Max(0f, duration), shieldVersion));
    }

    private System.Collections.IEnumerator ShieldRoutine(float duration, int version)
    {
        yield return new WaitForSeconds(duration);
        if (version != shieldVersion) yield break;
        isShielded = false;
        shieldHitPlayers.Clear();
        RefreshInvulnerability();
    }

    public void ApplyHuntingOrder(float duration)
    {
        huntingVersion++;
        canEatHigher = true;
        StartCoroutine(HuntingRoutine(Mathf.Max(0f, duration), huntingVersion));
    }

    private System.Collections.IEnumerator HuntingRoutine(float duration, int version)
    {
        yield return new WaitForSeconds(duration);
        if (version == huntingVersion) canEatHigher = false;
    }

    public void ApplyAreaSlow(float multiplier, float duration)
    {
        if (Time.time >= areaSlowUntil) areaSlowFactor = 1f;
        areaSlowFactor = Mathf.Min(areaSlowFactor, Mathf.Clamp01(multiplier));
        areaSlowUntil = Mathf.Max(areaSlowUntil, Time.time + Mathf.Max(0.01f, duration));
    }

    public void ApplySpeedMultiplier(float multiplier, float duration)
    {
        float safeMultiplier = Mathf.Max(0f, multiplier);
        if (safeMultiplier < 1f)
        {
            slowVersion++;
            temporarySlowFactor = safeMultiplier;
            StartCoroutine(SpeedRoutine(Mathf.Max(0f, duration), slowVersion, true));
        }
        else
        {
            buffVersion++;
            temporarySpeedBoost = safeMultiplier;
            StartCoroutine(SpeedRoutine(Mathf.Max(0f, duration), buffVersion, false));
        }
    }

    private System.Collections.IEnumerator SpeedRoutine(float duration, int version, bool isSlow)
    {
        yield return new WaitForSeconds(duration);
        if (isSlow)
        {
            if (version == slowVersion) temporarySlowFactor = 1f;
        }
        else if (version == buffVersion) temporarySpeedBoost = 1f;
    }

    public void ApplyInvisibility(float duration)
    {
        invisibleVersion++;

        // 只在第一次进入隐身时记录真实的原始透明度。
        // 连续获取隐身道具时刷新计时，不会把0.75再次乘0.75。
        if (!isInvisible || invisibilityRenderers == null)
        {
            invisibilityRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            invisibleOriginalAlphas = new float[invisibilityRenderers.Length];
            for (int i = 0; i < invisibilityRenderers.Length; i++)
                invisibleOriginalAlphas[i] = invisibilityRenderers[i] != null ? invisibilityRenderers[i].color.a : 1f;
        }

        isInvisible = true;
        float alphaMultiplier = Mathf.Clamp01(invisibilityOpacity);
        for (int i = 0; i < invisibilityRenderers.Length; i++)
        {
            SpriteRenderer sprite = invisibilityRenderers[i];
            if (sprite == null) continue;
            Color color = sprite.color;
            color.a = invisibleOriginalAlphas[i] * alphaMultiplier;
            sprite.color = color;
        }

        RefreshInvulnerability();
        StartCoroutine(InvisibilityRoutine(Mathf.Max(0f, duration), invisibleVersion));
    }

    private System.Collections.IEnumerator InvisibilityRoutine(float duration, int version)
    {
        yield return new WaitForSeconds(duration);
        if (version != invisibleVersion) yield break;
        RestoreInvisibilityVisuals();
    }

    private void RestoreInvisibilityVisuals()
    {
        if (invisibilityRenderers != null && invisibleOriginalAlphas != null)
        {
            for (int i = 0; i < invisibilityRenderers.Length && i < invisibleOriginalAlphas.Length; i++)
            {
                SpriteRenderer sprite = invisibilityRenderers[i];
                if (sprite == null) continue;
                Color color = sprite.color;
                color.a = invisibleOriginalAlphas[i];
                sprite.color = color;
            }
        }

        invisibilityRenderers = null;
        invisibleOriginalAlphas = null;
        isInvisible = false;
        RefreshInvulnerability();
    }

}