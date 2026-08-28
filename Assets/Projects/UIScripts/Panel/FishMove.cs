using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public enum FishControlMode
{
    AI = 0,
    LocalKeyboard = 1,
    RemoteMqtt = 2
}

/// <summary>
/// 单条鱼的移动、玩家输入、动画和碰撞逻辑。
/// 支持 AI、本地键盘和 MQTT 远程三种控制模式。
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Collider2D))]
public class FishMove : MonoBehaviour
{
    [Header("组件")]
    public Collider2D fishCollider;
    public Rigidbody2D fishRigidbody;
    public SkeletonGraphic skeletonGraphic;
    public Text LevelText;

    [Header("动画")]
    [SpineAnimation]
    public string idleAnimation = "idle";

    [Header("AI 移动")]
    [Min(0f)]
    public float minMoveSpeed = 50f;

    [Min(0f)]
    public float maxMoveSpeed = 100f;

    [Header("玩家移动")]
    [Min(0f)]
    public float playerMoveSpeed = 180f;

    [Header("玩家加速")]
    [Tooltip("触发加速的按键")]
    public KeyCode boostKey = KeyCode.Space;

    [Tooltip("加速持续时间，单位为秒")]
    [Min(0.01f)]
    public float boostDuration = 3f;

    [Tooltip("加速时玩家移动速度倍率")]
    [Min(1f)]
    public float boostSpeedMultiplier = 2f;

    [Tooltip("加速时 Spine 动画播放速度倍率")]
    [Min(1f)]
    public float boostAnimationSpeedMultiplier = 2f;

    [Tooltip("输入小于该值时视为没有输入")]
    [Range(0f, 1f)]
    public float inputDeadZone = 0.1f;

    [Header("朝向")]
    [Tooltip("鱼原始图片是否朝向右边")]
    public bool facesRightByDefault = true;

    [Tooltip("AI 鱼碰到上下边缘时是否反弹")]
    public bool bounceVertically = true;

    [Header("边界")]
    [Tooltip("鱼和移动区域边缘之间预留的距离")]
    [Min(0f)]
    public float boundaryPadding = 20f;

    public int PowerIndex { get; private set; }
    public int CurrentEatCount { get; private set; }
    public int RequiredEatCount { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsBeingEaten { get; private set; }
    public FishControlMode ControlMode { get; private set; }
    public long PlayerId { get; private set; }
    public string PlayerNickName { get; private set; }

    public bool IsPlayer
    {
        get { return ControlMode != FishControlMode.AI; }
    }

    public bool IsLocalKeyboardPlayer
    {
        get { return ControlMode == FishControlMode.LocalKeyboard; }
    }

    public bool IsRemoteMqttPlayer
    {
        get { return ControlMode == FishControlMode.RemoteMqtt; }
    }

    public Vector2 MoveDirection
    {
        get { return moveDirection; }
    }

    public float MoveStrength
    {
        get { return moveStrength; }
    }

    private FishGameManager manager;
    private RectTransform rectTransform;
    private Vector2 moveDirection;
    private bool canMove;
    private float currentMoveSpeed;
    private Vector3 originalLocalScale;
    private bool isBoosting;
    private bool boostUsesTimer;
    private float boostEndTime;
    private float moveStrength = 1f;

    public bool IsBoosting
    {
        get { return isBoosting; }
    }

    /// <summary>
    /// 当前加速剩余时间。未加速时返回 0。
    /// FishGameManager 会在玩家升级换鱼时读取它。
    /// </summary>
    public float SpeedBoostRemainingTime
    {
        get
        {
            if (!isBoosting || !boostUsesTimer)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                boostEndTime - Time.time
            );
        }
    }

    [Header("吃鱼反馈动画")]
    [Tooltip("玩家吃到鱼时放大的倍率")]
    [Min(1f)]
    public float eatScaleMultiplier = 1.15f;

    [Tooltip("吃鱼变大再恢复的总时间")]
    [Min(0.01f)]
    public float eatScaleDuration = 0.2f;

    [Header("转向")]
    [Tooltip("鱼旋转插值速度")]
    [Min(0f)]
    public float turnLerpSpeed = 8f;

    [Tooltip("鱼身允许的最大上下倾斜角度。限制在 90 度以内可避免倒立")]
    [Range(0f, 85f)]
    public float maxTiltAngle = 30f;

    [Tooltip("接近垂直移动时保持当前左右朝向，避免频繁翻转")]
    [Range(0f, 0.5f)]
    public float horizontalFlipDeadZone = 0.05f;

    private float targetRotationZ;
    private bool facingLeft;
    private float skeletonBaseScaleX = 1f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (fishCollider == null)
        {
            fishCollider = GetComponent<Collider2D>();
        }

        if (fishRigidbody == null)
        {
            fishRigidbody = GetComponent<Rigidbody2D>();
        }

        if (skeletonGraphic == null)
        {
            skeletonGraphic = GetComponent<SkeletonGraphic>();
        }

        if (LevelText == null)
        {
            LevelText = GetComponentInChildren<Text>(true);
        }

        originalLocalScale = transform.localScale;
        CacheSkeletonBaseScale();
        ConfigureRigidbody();
    }

    private void OnEnable()
    {
        transform.localScale = originalLocalScale;
        transform.localRotation = Quaternion.identity;

        // 对象池会保留 RectTransform 的 Z。
        // UI 鱼统一固定在 Canvas 平面，避免 Game 相机裁剪。
        ResetRectTransformDepth();

        targetRotationZ = 0f;
        facingLeft = false;

        ResetRuntimeState();

        if (skeletonGraphic != null)
        {
            skeletonGraphic.Initialize(false);
            CacheSkeletonBaseScale();
            SetFishFacingLeft(false);
            PlayIdleAnimation();
        }
    }

    private void Update()
    {
        UpdateBoostState();

        if (!canMove ||
            IsBusy ||
            IsBeingEaten ||
            manager == null)
        {
            return;
        }

        if (IsLocalKeyboardPlayer)
        {
            UpdatePlayerInput();
        }

        MoveFish();

        UpdateTurnAnimation();
    }

    /// <summary>
    /// 兼容旧调用：isPlayer=true 视为本地键盘玩家鱼。
    /// </summary>
    public void InitializeFish(
        FishGameManager fishManager,
        int powerIndex,
        int requiredEatCount,
        bool isPlayer)
    {
        InitializeFish(
            fishManager,
            powerIndex,
            requiredEatCount,
            isPlayer
                ? FishControlMode.LocalKeyboard
                : FishControlMode.AI,
            0L,
            string.Empty
        );
    }

    /// <summary>
    /// 每次从对象池生成后，由管理器调用。
    /// </summary>
    public void InitializeFish(
        FishGameManager fishManager,
        int powerIndex,
        int requiredEatCount,
        FishControlMode controlMode,
        long playerId = 0L,
        string playerNickName = "")
    {
        manager = fishManager;
        PowerIndex = powerIndex;
        RequiredEatCount = Mathf.Max(1, requiredEatCount);
        ControlMode = controlMode;
        PlayerId = playerId;
        PlayerNickName = playerNickName ?? string.Empty;

        ResetBoostState();

        CurrentEatCount = 0;
        currentMoveSpeed = IsPlayer
            ? playerMoveSpeed
            : Random.Range(minMoveSpeed, maxMoveSpeed);
        moveStrength = IsPlayer ? 0f : 1f;

        IsBusy = false;
        IsBeingEaten = false;

        if (fishCollider != null)
        {
            fishCollider.enabled = true;
            fishCollider.isTrigger = true;
        }

        UpdateLevelText();
        PlayIdleAnimation();
        ApplyFacingDirection();
    }

    private void ConfigureRigidbody()
    {
        if (fishRigidbody == null)
        {
            return;
        }

        fishRigidbody.simulated = true;
        fishRigidbody.bodyType = RigidbodyType2D.Kinematic;
        fishRigidbody.gravityScale = 0f;
        fishRigidbody.velocity = Vector2.zero;
        fishRigidbody.angularVelocity = 0f;
        fishRigidbody.freezeRotation = true;
        fishRigidbody.useFullKinematicContacts = true;
    }

    private void ResetRuntimeState()
    {
        canMove = false;
        IsBusy = false;
        IsBeingEaten = false;
        ControlMode = FishControlMode.AI;
        PlayerId = 0L;
        PlayerNickName = string.Empty;
        CurrentEatCount = 0;
        moveDirection = Vector2.right;
        moveStrength = 1f;

        if (fishCollider != null)
        {
            fishCollider.enabled = true;
            fishCollider.isTrigger = true;
        }

        if (fishRigidbody != null)
        {
            fishRigidbody.simulated = true;
            fishRigidbody.velocity = Vector2.zero;
            fishRigidbody.angularVelocity = 0f;
        }
    }

    private void UpdatePlayerInput()
    {
        if (Input.GetKeyDown(boostKey))
        {
            StartBoost();
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector2 inputDirection =
            new Vector2(horizontal, vertical);

        if (inputDirection.sqrMagnitude <
            inputDeadZone * inputDeadZone)
        {
            SetMoveInput(Vector2.zero, 0f);
            return;
        }

        inputDirection.Normalize();
        SetMoveInput(inputDirection, 1f);
    }

    private void MoveFish()
    {
        if (rectTransform == null ||
            moveDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        float speed;

        if (IsPlayer)
        {
            float speedMultiplier =
                isBoosting
                    ? Mathf.Max(1f, boostSpeedMultiplier)
                    : 1f;

            speed =
                playerMoveSpeed *
                speedMultiplier *
                Mathf.Clamp01(moveStrength);
        }
        else
        {
            speed = currentMoveSpeed;
        }

        Vector2 nextPosition =
            rectTransform.anchoredPosition +
            moveDirection *
            speed *
            Time.deltaTime;

        Rect movementRect = manager.GetMovementRect();
        Vector2 halfSize = rectTransform.rect.size * 0.5f;

        float minX =
            movementRect.xMin +
            halfSize.x +
            boundaryPadding;

        float maxX =
            movementRect.xMax -
            halfSize.x -
            boundaryPadding;

        float minY =
            movementRect.yMin +
            halfSize.y +
            boundaryPadding;

        float maxY =
            movementRect.yMax -
            halfSize.y -
            boundaryPadding;

        if (IsPlayer)
        {
            nextPosition.x =
                Mathf.Clamp(nextPosition.x, minX, maxX);

            nextPosition.y =
                Mathf.Clamp(nextPosition.y, minY, maxY);
        }
        else
        {
            if (nextPosition.x <= minX)
            {
                nextPosition.x = minX;
                moveDirection.x =
                    Mathf.Abs(moveDirection.x);
                ApplyFacingDirection();
            }
            else if (nextPosition.x >= maxX)
            {
                nextPosition.x = maxX;
                moveDirection.x =
                    -Mathf.Abs(moveDirection.x);
                ApplyFacingDirection();
            }

            if (bounceVertically)
            {
                if (nextPosition.y <= minY)
                {
                    nextPosition.y = minY;
                    moveDirection.y =
                        Mathf.Abs(moveDirection.y);
                    ApplyFacingDirection();
                }
                else if (nextPosition.y >= maxY)
                {
                    nextPosition.y = maxY;
                    moveDirection.y =
                        -Mathf.Abs(moveDirection.y);
                    ApplyFacingDirection();
                }
            }
            else
            {
                nextPosition.y =
                    Mathf.Clamp(nextPosition.y, minY, maxY);
            }
        }

        rectTransform.anchoredPosition = nextPosition;
    }

    /// <summary>
    /// 开始玩家加速。重复按键会重新计算持续时间。
    /// </summary>
    private void StartBoost()
    {
        StartSpeedBoost(boostDuration);
    }

    /// <summary>
    /// 开始一次指定时长的加速。
    /// 可由 FishGameManager 在玩家升级换鱼后继续剩余加速时间。
    /// </summary>
    public void StartSpeedBoost(float duration)
    {
        if (!IsPlayer)
        {
            return;
        }

        float safeDuration =
            Mathf.Max(0f, duration);

        if (safeDuration <= 0f)
        {
            StopBoost();
            return;
        }

        isBoosting = true;
        boostUsesTimer = true;
        boostEndTime =
            Time.time +
            safeDuration;

        ApplyAnimationPlaybackSpeed();
    }

    /// <summary>
    /// 检查加速是否结束。
    /// </summary>
    private void UpdateBoostState()
    {
        if (!isBoosting)
        {
            return;
        }

        if (!IsPlayer ||
            (boostUsesTimer &&
             Time.time >= boostEndTime))
        {
            StopBoost();
        }
    }

    private void StopBoost()
    {
        isBoosting = false;
        boostUsesTimer = false;
        boostEndTime = 0f;
        ApplyAnimationPlaybackSpeed();
    }

    /// <summary>
    /// MQTT 加速按下/松开。pressed=true 会一直保持到收到 false。
    /// </summary>
    public void SetExternalBoostPressed(bool pressed)
    {
        if (!IsPlayer)
        {
            return;
        }

        if (!pressed)
        {
            StopBoost();
            return;
        }

        isBoosting = true;
        boostUsesTimer = false;
        boostEndTime = 0f;
        ApplyAnimationPlaybackSpeed();
    }

    private void ResetBoostState()
    {
        isBoosting = false;
        boostUsesTimer = false;
        boostEndTime = 0f;
        ApplyAnimationPlaybackSpeed();
    }

    /// <summary>
    /// 让 Spine 动画速度和玩家加速状态同步。
    /// </summary>
    private void ApplyAnimationPlaybackSpeed()
    {
        if (skeletonGraphic == null ||
            skeletonGraphic.AnimationState == null)
        {
            return;
        }

        skeletonGraphic.AnimationState.TimeScale =
            IsPlayer && isBoosting
                ? Mathf.Max(
                    1f,
                    boostAnimationSpeedMultiplier
                )
                : 1f;
    }

    public void SetRandomMoveDirection()
    {
        if (IsPlayer)
        {
            moveDirection = Vector2.zero;
            return;
        }

        float horizontalDirection =
            Random.value < 0.5f ? -1f : 1f;

        float verticalDirection =
            Random.Range(-0.35f, 0.35f);

        SetMoveDirection(
            new Vector2(
                horizontalDirection,
                verticalDirection
            )
        );
    }

    public void SetMoveDirection(Vector2 direction)
    {
        SetMoveInput(
            direction,
            direction.sqrMagnitude < 0.001f ? 0f : 1f
        );
    }

    /// <summary>
    /// 设置移动方向和力度。方向会归一化，力度保持 0~1。
    /// </summary>
    public void SetMoveInput(
        Vector2 direction,
        float strength)
    {
        moveStrength = Mathf.Clamp01(strength);

        if (direction.sqrMagnitude < 0.001f ||
            moveStrength <= 0f)
        {
            moveDirection = Vector2.zero;
            moveStrength = 0f;
            return;
        }

        moveDirection = direction.normalized;
        ApplyFacingDirection();
    }

    /// <summary>
    /// MQTT 摇杆输入。协议角度：0=右，90=下，180=左，270=上。
    /// </summary>
    public void SetRemoteJoystick(
        int angle,
        float force,
        bool active)
    {
        if (!IsRemoteMqttPlayer)
        {
            return;
        }

        float safeForce = Mathf.Clamp01(force);

        if (!active || safeForce <= 0f)
        {
            SetMoveInput(Vector2.zero, 0f);
            return;
        }

        float normalizedAngle =
            Mathf.Repeat(angle, 360f);
        float radians =
            normalizedAngle * Mathf.Deg2Rad;

        Vector2 direction = new Vector2(
            Mathf.Cos(radians),
            -Mathf.Sin(radians)
        );

        SetMoveInput(direction, safeForce);
    }

    private void ApplyFacingDirection()
    {
        if (moveDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector2 dir = moveDirection.normalized;

        // 只在水平方向足够明确时切换左右朝向。
        // 接近垂直移动时保持当前朝向，避免 ScaleX 来回抖动。
        if (dir.x > horizontalFlipDeadZone)
        {
            SetFishFacingLeft(false);
        }
        else if (dir.x < -horizontalFlipDeadZone)
        {
            SetFishFacingLeft(true);
        }

        float movementAngle =
            Mathf.Atan2(dir.y, dir.x) *
            Mathf.Rad2Deg;

        /*
         * 鱼默认朝右时：
         * 右侧移动由 Z 轴旋转负责。
         * 左侧移动先用 Skeleton.ScaleX 镜像，再只旋转剩余的小角度。
         *
         * 例如移动角度为 160 度：
         * ScaleX 负责 180 度翻面；
         * Z 轴只需要旋转 -20 度，而不是 -160 度。
         */
        float localTiltAngle;

        if (facingLeft)
        {
            localTiltAngle =
                Mathf.DeltaAngle(
                    180f,
                    movementAngle
                );
        }
        else
        {
            localTiltAngle =
                Mathf.DeltaAngle(
                    0f,
                    movementAngle
                );
        }

        targetRotationZ =
            Mathf.Clamp(
                localTiltAngle,
                -maxTiltAngle,
                maxTiltAngle
            );
    }

    private void CacheSkeletonBaseScale()
    {
        if (skeletonGraphic == null ||
            skeletonGraphic.Skeleton == null)
        {
            return;
        }

        float absoluteScaleX =
            Mathf.Abs(
                skeletonGraphic.Skeleton.ScaleX
            );

        skeletonBaseScaleX =
            absoluteScaleX > 0.0001f
                ? absoluteScaleX
                : 1f;
    }

    private void SetFishFacingLeft(bool left)
    {
        facingLeft = left;

        if (skeletonGraphic == null ||
            skeletonGraphic.Skeleton == null)
        {
            return;
        }

        bool shouldMirror =
            facesRightByDefault
                ? left
                : !left;

        skeletonGraphic.Skeleton.ScaleX =
            skeletonBaseScaleX *
            (shouldMirror ? -1f : 1f);
    }

    private void UpdateTurnAnimation()
    {
        float currentRotationZ =
            Mathf.DeltaAngle(
                0f,
                transform.localEulerAngles.z
            );

        float lerpFactor =
            1f -
            Mathf.Exp(
                -turnLerpSpeed *
                Time.deltaTime
            );

        float newRotationZ =
            Mathf.LerpAngle(
                currentRotationZ,
                targetRotationZ,
                lerpFactor
            );

        // 这里只允许绕 Z 轴做小角度倾斜。
        // 左右 180 度由 Skeleton.ScaleX 负责，因此不会再出现倒立。
        transform.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                newRotationZ
            );
    }

    /// <summary>
    /// 将 UI 鱼固定到 Canvas 的 Z=0 平面。
    /// anchoredPosition 是 Vector2，单独赋值不会改变原来的 Z。
    /// </summary>
    private void ResetRectTransformDepth()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (rectTransform == null)
        {
            return;
        }

        Vector3 position3D =
            rectTransform.anchoredPosition3D;

        position3D.z = 0f;
        rectTransform.anchoredPosition3D = position3D;
    }

    /// <summary>
    /// 升级或对象池复用后刷新 SkeletonGraphic 的 UI 网格。
    /// </summary>
    public void RefreshUiGraphic()
    {
        ResetRectTransformDepth();

        if (skeletonGraphic == null)
        {
            return;
        }

        skeletonGraphic.SetVerticesDirty();
        skeletonGraphic.SetMaterialDirty();
    }

    private void UpdateLevelText()
    {
        if (LevelText == null)
        {
            return;
        }

        LevelText.text = "等级 " + (PowerIndex + 1);

        if (IsLocalKeyboardPlayer)
        {
            LevelText.text += "（本地）";
        }
        else if (IsRemoteMqttPlayer)
        {
            if (!string.IsNullOrEmpty(PlayerNickName))
            {
                LevelText.text += "（" + PlayerNickName + "）";
            }
            else
            {
                LevelText.text += "（远程）";
            }
        }
    }

    public void StopMovement()
    {
        canMove = false;

        if (fishRigidbody != null)
        {
            fishRigidbody.velocity = Vector2.zero;
        }
    }

    public void ResumeMovement()
    {
        if (IsBusy || IsBeingEaten)
        {
            return;
        }

        canMove = true;
    }

    public bool CanEat(FishMove victim)
    {
        if (!IsPlayer || victim == null || victim.IsPlayer)
        {
            return false;
        }

        if (IsBusy ||
            IsBeingEaten ||
            victim.IsBusy ||
            victim.IsBeingEaten)
        {
            return false;
        }

        return PowerIndex > victim.PowerIndex;
    }

    /// <summary>
    /// 只有玩家鱼可以立即吃掉比自己等级低的 AI 鱼。
    /// </summary>
    public void BeginEat(FishMove victim)
    {
        if (!CanEat(victim))
        {
            return;
        }

        IsBusy = true;
        StopMovement();
        FaceTarget(victim);
        victim.PrepareToBeEaten();

        if (manager == null)
        {
            manager = FishGameManager.Instance;
        }

        if (manager == null)
        {
            Debug.LogError(
                "[FishMove] 找不到 FishGameManager。",
                gameObject
            );

            IsBusy = false;
            ResumeMovement();
            return;
        }

        PlayEatScaleAnimation();

        manager.CompleteEat(this, victim);
    }

    /// <summary>
    /// 玩家吃到鱼时的缩放反馈。
    /// 使用 DOTween 在指定时间内完成放大和恢复。
    /// </summary>
    private void PlayEatScaleAnimation()
    {
        if (!IsPlayer)
        {
            return;
        }

        transform.DOKill();

        Vector3 targetScale =
            originalLocalScale * eatScaleMultiplier;

        Sequence sequence = DOTween.Sequence();

        sequence.Append(
            transform.DOScale(
                targetScale,
                eatScaleDuration * 0.5f
            )
            .SetEase(Ease.OutQuad)
        );

        sequence.Append(
            transform.DOScale(
                originalLocalScale,
                eatScaleDuration * 0.5f
            )
            .SetEase(Ease.InQuad)
        );
    }

    private void FaceTarget(FishMove target)
    {
        if (target == null)
        {
            return;
        }

        float horizontalDifference =
            target.transform.position.x -
            transform.position.x;

        if (Mathf.Abs(horizontalDifference) < 0.001f)
        {
            return;
        }

        Vector2 newDirection = moveDirection;
        newDirection.x =
            horizontalDifference > 0f ? 1f : -1f;

        if (newDirection.sqrMagnitude < 0.001f)
        {
            newDirection = Vector2.right;
        }

        SetMoveDirection(newDirection);
    }

    private void PrepareToBeEaten()
    {
        IsBeingEaten = true;
        IsBusy = true;
        StopMovement();

        if (fishCollider != null)
        {
            fishCollider.enabled = false;
        }
    }

    public void AddEatCount()
    {
        CurrentEatCount++;
    }

    public bool HasReachedUpgradeCondition()
    {
        return CurrentEatCount >= RequiredEatCount;
    }

    public void ResetEatCount()
    {
        CurrentEatCount = 0;
    }

    public void FinishEatWithoutUpgrade()
    {
        IsBusy = false;
        IsBeingEaten = false;
        PlayIdleAnimation();
        ResumeMovement();
    }

    public void PlayIdleAnimation()
    {
        if (skeletonGraphic == null ||
            string.IsNullOrEmpty(idleAnimation))
        {
            return;
        }

        skeletonGraphic.Initialize(false);
        CacheSkeletonBaseScale();
        SetFishFacingLeft(facingLeft);

        if (skeletonGraphic.AnimationState == null)
        {
            return;
        }

        Spine.Animation animation =
            skeletonGraphic.SkeletonData
                .FindAnimation(idleAnimation);

        if (animation == null)
        {
            Debug.LogWarning(
                "[FishMove] 找不到游动动画：" +
                idleAnimation,
                gameObject
            );
            return;
        }

        skeletonGraphic.AnimationState
            .SetAnimation(0, idleAnimation, true);

        ApplyAnimationPlaybackSpeed();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (manager == null)
        {
            manager = FishGameManager.Instance;
        }

        if (manager == null)
        {
            return;
        }

        FishMove otherFish =
            other.GetComponent<FishMove>();

        if (otherFish == null)
        {
            otherFish =
                other.GetComponentInParent<FishMove>();
        }

        if (otherFish == null ||
            otherFish == this)
        {
            return;
        }

        manager.TryStartEating(this, otherFish);
    }

    private void OnDisable()
    {
        canMove = false;
        IsBusy = false;
        IsBeingEaten = false;
        ControlMode = FishControlMode.AI;
        PlayerId = 0L;
        PlayerNickName = string.Empty;
        isBoosting = false;
        boostUsesTimer = false;
        boostEndTime = 0f;
        moveStrength = 1f;
        transform.localScale = originalLocalScale;
        transform.localRotation = Quaternion.identity;
        ResetRectTransformDepth();
        targetRotationZ = 0f;
        facingLeft = false;

        if (skeletonGraphic != null &&
            skeletonGraphic.Skeleton != null)
        {
            SetFishFacingLeft(false);

            if (skeletonGraphic.AnimationState != null)
            {
                skeletonGraphic.AnimationState.TimeScale = 1f;
            }
        }

        if (fishRigidbody != null)
        {
            fishRigidbody.velocity = Vector2.zero;
            fishRigidbody.angularVelocity = 0f;
        }
    }

    [ContextMenu("设置碰撞体大小")]
    public void SetColliderSize()
    {
        if (fishCollider == null)
        {
            fishCollider = GetComponent<Collider2D>();
        }

        BoxCollider2D boxCollider =
            fishCollider as BoxCollider2D;

        if (boxCollider == null)
        {
            Debug.LogWarning(
                "[FishMove] 当前碰撞体不是 BoxCollider2D。",
                gameObject
            );
            return;
        }

        RectTransform currentRect =
            GetComponent<RectTransform>();

        boxCollider.size = currentRect.rect.size;
        boxCollider.offset = Vector2.zero;
        boxCollider.isTrigger = true;
    }
}
