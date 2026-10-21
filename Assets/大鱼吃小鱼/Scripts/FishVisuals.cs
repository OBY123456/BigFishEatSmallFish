using UnityEngine;
using TMPro;

public class FishVisuals : MonoBehaviour
{
    [Header("鱼身体的SpriteRenderer；留空时自动在子物体查找")]
    public SpriteRenderer spriteRenderer;
    [Header("用于实现左右转身倾斜的美术子节点；不旋转物理根节点")]
    public Transform visualTransform;
    [Header("鱼游泳动画Animator；留空时自动查找")]
    public Animator animator;
    [Header("鱼头旁边的3D TextMeshPro文本；留空时自动查找")]
    public TextMeshPro levelText;

    [Header("原始Sprite不翻转时是否头朝右")]
    public bool facesRightByDefault = true;
    [Header("鱼游动时视觉倾斜的最大角度")]
    [Range(0f, 85f)] public float maxTiltAngle = 30f;
    [Header("鱼转身时的平滑速度，数值越大越快")]
    public float turnLerpSpeed = 8f;

    [Header("开启后根据SpriteRenderer.flipX镜像鱼头文字偏移，不改变文字本身朝向")]
    public bool mirrorLevelTextWithFish = true;

    [Header("吞噬时鱼向前轻推的距离，世界视觉局部单位")]
    [Min(0f)] public float eatPunchDistance = 0.18f;
    [Header("吞噬时鱼的放大比例，0.1表示最大放大10%")]
    [Range(0f, 0.5f)] public float eatPunchScale = 0.10f;
    [Header("吞噬前冲与回弹动画的总时间，单位秒")]
    [Min(0.05f)] public float eatPunchDuration = 0.28f;
    [Header("吞噬前冲阶段所占的时间比例，越小前冲越快")]
    [Range(0.1f, 0.9f)] public float eatPunchForwardPart = 0.32f;
    [Header("吞噬时文字随鱼移动但不跟随放大")]
    public bool keepTextSizeDuringPunch = true;

    private Vector3 originalVisualLocalPosition;
    private Vector3 originalVisualLocalScale;
    private Vector3 originalTextLocalScale;
    private bool isTextInsideVisual;
    private bool eatPunchPlaying;
    private float eatPunchTime;
    private Vector2 eatPunchDirection = Vector2.right;

    private Vector3 initialTextOffsetInSpriteSpace;
    private bool initialSpriteFlipX;
    private bool hasTextAnchor;
    private MeshRenderer levelTextMeshRenderer;
    private GameObject levelAppearanceRoot;

    private float targetRotationZ;
    private float currentRotationZ;
    private bool facingLeft;
    private Quaternion initialLocalRotation;

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualTransform == null && spriteRenderer != null) visualTransform = spriteRenderer.transform;
        if (animator == null && visualTransform != null) animator = visualTransform.GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (levelText == null) levelText = GetComponentInChildren<TextMeshPro>();

        // 不旋转 Rigidbody2D 根节点，避免影响碰撞体和物理位置
        if (visualTransform == transform) visualTransform = null;
        if (visualTransform != null)
        {
            initialLocalRotation = visualTransform.localRotation;
            originalVisualLocalPosition = visualTransform.localPosition;
            originalVisualLocalScale = visualTransform.localScale;
        }

        if (levelText != null)
        {
            originalTextLocalScale = levelText.transform.localScale;
            isTextInsideVisual = visualTransform != null && levelText.transform.IsChildOf(visualTransform);
        }

        // SpriteRenderer.flipX只镜像鱼的图片，不会自动改变文字子物体的坐标。
        // 在Prefab原始姿态下记录文字相对于Sprite枢轴的位置，之后以固定原点做镜像，避免积累误差。
        if (spriteRenderer != null && levelText != null)
        {
            initialTextOffsetInSpriteSpace = spriteRenderer.transform.InverseTransformPoint(levelText.transform.position);
            initialSpriteFlipX = spriteRenderer.flipX;
            levelTextMeshRenderer = levelText.GetComponent<MeshRenderer>();
            hasTextAnchor = true;
        }
    }

    void OnEnable()
    {
        ResetEatPunch();
        targetRotationZ = 0f;
        currentRotationZ = 0f;
        facingLeft = false;
        if (spriteRenderer != null) spriteRenderer.flipX = !facesRightByDefault;
        UpdateLevelTextHeadPosition();
    }

    void OnDisable()
    {
        ResetEatPunch();
    }

    void LateUpdate()
    {
        if (visualTransform != null)
        {
            float lerpFactor = 1f - Mathf.Exp(-turnLerpSpeed * Time.deltaTime);
            currentRotationZ = Mathf.LerpAngle(currentRotationZ, targetRotationZ, lerpFactor);
            visualTransform.localRotation = initialLocalRotation * Quaternion.Euler(0f, 0f, currentRotationZ);
        }

        UpdateEatPunch();
        UpdateLevelTextHeadPosition();
    }

    public void PlayEatPunch()
    {
        if (visualTransform == null) return;

        // 只移动鱼的美术节点，不影响 Rigidbody2D、碰撞体或 MQTT 移动。
        // 每次重新从原始位置开始，不叠加上一次的前冲位移。
        eatPunchTime = 0f;
        eatPunchDirection = facingLeft ? Vector2.left : Vector2.right;
        eatPunchPlaying = true;
    }

    public void PlayEatPunchTowards(Vector3 targetWorldPosition)
    {
        if (visualTransform == null) return;

        Vector2 worldDirection = (Vector2)(targetWorldPosition - transform.position);
        if (worldDirection.sqrMagnitude <= 0.000001f)
        {
            PlayEatPunch();
            return;
        }

        worldDirection.Normalize();

        // 第一步：先立即翻转 SpriteRenderer，并调整美术朝向指向猎物。
        ApplyFacingDirection(worldDirection);
        currentRotationZ = targetRotationZ;
        visualTransform.localRotation = initialLocalRotation *
            Quaternion.Euler(0f, 0f, currentRotationZ);
        UpdateLevelTextHeadPosition();

        // 第二步：再播放前冲、放大、回弹动画。
        // 将世界方向转换到美术节点的父空间（包含缩放补偿），使上下方猎物也能正确前冲。
        PlayEatPunch();
        Vector3 localDirection = visualTransform.parent != null
            ? visualTransform.parent.InverseTransformVector(new Vector3(worldDirection.x, worldDirection.y, 0f))
            : new Vector3(worldDirection.x, worldDirection.y, 0f);
        Vector2 forward = new Vector2(localDirection.x, localDirection.y);
        if (forward.sqrMagnitude > 0.000001f) eatPunchDirection = forward.normalized;
    }

    private void UpdateEatPunch()
    {
        if (!eatPunchPlaying || visualTransform == null) return;

        float duration = Mathf.Max(0.05f, eatPunchDuration);
        eatPunchTime += Time.deltaTime;
        float progress = Mathf.Clamp01(eatPunchTime / duration);
        float forwardPart = Mathf.Clamp(eatPunchForwardPart, 0.1f, 0.9f);
        float amount;

        if (progress <= forwardPart)
            amount = Mathf.SmoothStep(0f, 1f, progress / forwardPart);
        else
            amount = 1f - Mathf.SmoothStep(0f, 1f, (progress - forwardPart) / (1f - forwardPart));

        visualTransform.localPosition = originalVisualLocalPosition +
            new Vector3(eatPunchDirection.x, eatPunchDirection.y, 0f) *
            (Mathf.Max(0f, eatPunchDistance) * amount);

        float scale = 1f + Mathf.Max(0f, eatPunchScale) * amount;
        visualTransform.localScale = originalVisualLocalScale * scale;

        // 如果文字是美术节点的子物体，反向补偿缩放，保持文字大小稳定。
        if (keepTextSizeDuringPunch && isTextInsideVisual && levelText != null)
            levelText.transform.localScale = originalTextLocalScale / scale;

        if (progress >= 1f) ResetEatPunch();
    }

    private void ResetEatPunch()
    {
        eatPunchPlaying = false;
        eatPunchTime = 0f;

        if (visualTransform != null)
        {
            visualTransform.localPosition = originalVisualLocalPosition;
            visualTransform.localScale = originalVisualLocalScale;
        }

        if (isTextInsideVisual && levelText != null)
            levelText.transform.localScale = originalTextLocalScale;
    }

    private void UpdateLevelTextHeadPosition()
    {
        if (!mirrorLevelTextWithFish || !hasTextAnchor || spriteRenderer == null || levelText == null) return;

        // 镜像仅作用于相对Sprite枢轴的横向偏移，垂直高度与深度保持Prefab配置。
        Vector3 desiredLocalOffset = initialTextOffsetInSpriteSpace;
        if (spriteRenderer.flipX != initialSpriteFlipX) desiredLocalOffset.x = -desiredLocalOffset.x;

        // 通过世界坐标转换回文字父物体，适配文字与图片不在同一层级的Prefab。
        Vector3 desiredWorldPosition = spriteRenderer.transform.TransformPoint(desiredLocalOffset);
        Transform textParent = levelText.transform.parent;
        if (textParent != null)
            levelText.transform.localPosition = textParent.InverseTransformPoint(desiredWorldPosition);
        else
            levelText.transform.position = desiredWorldPosition;
    }

    /// <summary>
    /// 用 PlayerFish 对应等级Prefab的美术层替换当前鱼的美术层，保留当前 FishCore、
    /// Rigidbody2D、碰撞体、MQTT控制和所有玩法状态。
    /// </summary>
    public void SetPlayerLevelAppearance(GameObject playerPrefab, int level, int score, string playerName)
    {
        if (playerPrefab == null) return;

        GameObject sample = Instantiate(playerPrefab);
        FishVisuals sourceVisuals = sample.GetComponent<FishVisuals>();
        if (sourceVisuals == null) sourceVisuals = sample.GetComponentInChildren<FishVisuals>(true);

        if (sourceVisuals == null)
        {
            Debug.LogWarning("等级鱼Prefab缺少 FishVisuals：" + playerPrefab.name);
            Destroy(sample);
            return;
        }

        Transform sourceVisualRoot = sourceVisuals.visualTransform;
        if (sourceVisualRoot == null && sourceVisuals.spriteRenderer != null)
            sourceVisualRoot = sourceVisuals.spriteRenderer.transform;

        if (sourceVisualRoot == null)
        {
            Debug.LogWarning("等级鱼Prefab找不到可替换的美术节点：" + playerPrefab.name);
            Destroy(sample);
            return;
        }

        Transform oldVisualRoot = visualTransform;
        if (oldVisualRoot == null && spriteRenderer != null && spriteRenderer.transform != transform)
            oldVisualRoot = spriteRenderer.transform;

        TextMeshPro oldText = levelText;
        bool sourceTextInsideVisual = sourceVisuals.levelText != null &&
            sourceVisuals.levelText.transform.IsChildOf(sourceVisualRoot);

        GameObject replacement;
        if (sourceVisualRoot == sample.transform)
        {
            // 兼容 SpriteRenderer 直接挂在Prefab根节点的结构：克隆整棵Prefab，
            // 随后关闭克隆上的玩法/物理脚本，只保留其渲染与动画。
            replacement = Instantiate(sample, transform, false);
            MonoBehaviour[] scripts = replacement.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < scripts.Length; i++)
            {
                if (scripts[i] != null) scripts[i].enabled = false;
            }
            Collider2D[] colliders = replacement.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
            Rigidbody2D[] bodies = replacement.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++) bodies[i].simulated = false;
        }
        else
        {
            replacement = Instantiate(sourceVisualRoot.gameObject, transform, false);
        }

        // 若等级Prefab的名字文本不在美术节点内部，则单独复制文本节点。
        TextMeshPro replacementText = replacement.GetComponentInChildren<TextMeshPro>(true);
        if (!sourceTextInsideVisual && sourceVisuals.levelText != null)
        {
            replacementText = Instantiate(sourceVisuals.levelText.gameObject, transform, false)
                .GetComponent<TextMeshPro>();
            replacementText.transform.localPosition = sourceVisuals.levelText.transform.localPosition;
            replacementText.transform.localRotation = sourceVisuals.levelText.transform.localRotation;
            replacementText.transform.localScale = sourceVisuals.levelText.transform.localScale;
        }

        if (oldVisualRoot != null && oldVisualRoot != transform && oldVisualRoot.gameObject != replacement)
            Destroy(oldVisualRoot.gameObject);
        if (oldText != null && oldText.transform != transform &&
            (oldVisualRoot == null || !oldText.transform.IsChildOf(oldVisualRoot)) &&
            oldText.gameObject != replacement)
            Destroy(oldText.gameObject);

        if (levelAppearanceRoot != null && levelAppearanceRoot != replacement)
            Destroy(levelAppearanceRoot);
        levelAppearanceRoot = replacement;
        visualTransform = replacement.transform;
        spriteRenderer = replacement.GetComponentInChildren<SpriteRenderer>(true);
        animator = replacement.GetComponentInChildren<Animator>(true);
        levelText = replacementText;
        levelTextMeshRenderer = levelText != null ? levelText.GetComponent<MeshRenderer>() : null;

        initialLocalRotation = visualTransform.localRotation;
        originalVisualLocalPosition = visualTransform.localPosition;
        originalVisualLocalScale = visualTransform.localScale;
        originalTextLocalScale = levelText != null ? levelText.transform.localScale : Vector3.one;
        isTextInsideVisual = levelText != null && levelText.transform.IsChildOf(visualTransform);
        hasTextAnchor = spriteRenderer != null && levelText != null;
        if (hasTextAnchor)
        {
            initialTextOffsetInSpriteSpace = spriteRenderer.transform.InverseTransformPoint(levelText.transform.position);
            initialSpriteFlipX = spriteRenderer.flipX;
        }

        if (spriteRenderer != null)
            spriteRenderer.flipX = facesRightByDefault ? facingLeft : !facingLeft;

        UpdateText(level, score, playerName);
    }

    public void SetSortingOrder(int order)
    {
        if (spriteRenderer == null) return;

        // 由FishManager按鱼的创建顺序传入1、2、3……，同一鱼被对象池复用会再次获得新序号。
        spriteRenderer.sortingOrder = Mathf.Clamp(order, -32768, 32767);

        // 文本渲染器紧挨本鱼Sprite之上，避免被本鱼的贴图遮住。
        if (levelText != null)
        {
            if (levelTextMeshRenderer == null) levelTextMeshRenderer = levelText.GetComponent<MeshRenderer>();
            if (levelTextMeshRenderer != null)
            {
                levelTextMeshRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
                levelTextMeshRenderer.sortingOrder = Mathf.Min(32767, order + 1);
            }
        }

        UnityEngine.Rendering.SortingGroup sortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (sortingGroup != null) sortingGroup.sortingOrder = order;
    }

    public void UpdateText(int level, int experience, string playerName)
    {
        UpdateText(level, experience, playerName, 0L, !string.IsNullOrEmpty(playerName) && playerName != "NPC");
    }

    public void UpdateText(int level, int experience, string playerName, long playerId, bool isPlayer)
    {
        if (levelText == null) return;

        // 普通NPC和特殊鱼仍只显示等级；玩家使用昵称而不是玩家ID。
        if (!isPlayer || string.IsNullOrEmpty(playerName) || playerName == "NPC")
        {
            levelText.text = $"Lv.{level}";
        }
        else if (level >= 10)
        {
            // 满级不再显示经验。
            levelText.text = $"{playerName}_{level}级";
        }
        else
        {
            // LevelThresholds 的下标对应当前等级；满级前显示当前经验/升级所需经验。
            int thresholdIndex = Mathf.Clamp(level, 1, FishCore.LevelThresholds.Length - 1);
            int requiredExperience = Mathf.Max(1, FishCore.LevelThresholds[thresholdIndex]);
            levelText.text = $"{playerName}_{level}级_{Mathf.Max(0, experience)}/{requiredExperience}";
        }

        levelText.ForceMeshUpdate();
    }

    public void SetFacingDirection(Vector2 moveDir)
    {
        // 吃鱼动画未结束前锁住面朝猎物的方向，避免下一次键盘、MQTT 或 AI Tick 将鱼头转回。
        if (eatPunchPlaying) return;
        ApplyFacingDirection(moveDir);
    }

    private void ApplyFacingDirection(Vector2 moveDir)
    {
        if (spriteRenderer == null || moveDir.sqrMagnitude < 0.000001f) return;

        // 若目标恰好在正上/正下方，保留当前左右朝向，通过倾斜表示竖直方向。
        if (Mathf.Abs(moveDir.x) > 0.001f) facingLeft = moveDir.x < 0f;
        spriteRenderer.flipX = facesRightByDefault ? facingLeft : !facingLeft;

        float movementAngle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg;
        float referenceAngle = facingLeft ? 180f : 0f;
        float localTiltAngle = Mathf.DeltaAngle(referenceAngle, movementAngle);
        targetRotationZ = Mathf.Clamp(localTiltAngle, -maxTiltAngle, maxTiltAngle);
    }

    public void SetAnimationSpeed(float speed)
    {
        if (animator != null) animator.speed = speed;
    }
}
