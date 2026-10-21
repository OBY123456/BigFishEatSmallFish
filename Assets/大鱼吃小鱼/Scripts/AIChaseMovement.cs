using UnityEngine;
using Lean.Pool;

public class AIChaseMovement : MonoBehaviour, IMovement
{
    public bool IsActive { get; set; } = true;

    [Header("追逐玩家时距离目标小于此值就停下，避免左右抖动")]
    public float stopDistance = 0.05f;
    [Header("鲨鱼失去目标后的再次搜索间隔，秒")]
    public float searchInterval = 0.5f;
    [Header("鲨鱼追逐玩家的最大时间，秒")]
    public float chaseDuration = 60f;
    [Header("鲨鱼离场时相对于自身游动速度的倍率")]
    public float exitSpeedMultiplier = 1.3f;
    [Header("鲨鱼离开左右活动边界后额外游出的距离")]
    public float exitMargin = 1f;

    private Transform targetPlayer;
    private Rigidbody2D targetRigidbody;
    private FishCore targetCore;
    private FishCore selfCore;
    private FishVisuals visuals;
    private float nextSearchTime;
    private float chaseEndTime;
    private bool exiting;
    private Vector2 exitDirection;

    void Awake()
    {
        visuals = GetComponent<FishVisuals>();
        selfCore = GetComponent<FishCore>();
    }

    void OnEnable()
    {
        targetPlayer = null;
        targetRigidbody = null;
        targetCore = null;
        nextSearchTime = 0f;
        chaseEndTime = Time.time + Mathf.Max(1f, chaseDuration);
        exiting = false;
        exitDirection = Vector2.right;
    }

    public void Tick(Rigidbody2D rb, float currentSpeed)
    {
        if (!IsActive || rb == null || currentSpeed <= 0f) return;

        if (!exiting && Time.time >= chaseEndTime)
            BeginExit(rb.position);

        if (!exiting && !FindOrKeepTarget(rb.position))
        {
            // 玩家刚生成/重生时的 3 秒无敌期内不锁定玩家，也不因此立即离场。
            if (HasTemporarilyProtectedPlayer()) return;
            BeginExit(rb.position);
        }

        if (exiting)
        {
            MoveOutOfCamera(rb, currentSpeed * Mathf.Max(1f, exitSpeedMultiplier));
            return;
        }

        Vector2 targetPos = targetRigidbody != null ? targetRigidbody.position : (Vector2)targetPlayer.position;
        Vector2 toTarget = targetPos - rb.position;
        float distance = toTarget.magnitude;
        float remainingDistance = distance - Mathf.Max(0f, stopDistance);
        if (remainingDistance <= 0f) return;

        Vector2 direction = toTarget / Mathf.Max(distance, 0.0001f);
        Vector2 nextPos = rb.position + direction * Mathf.Min(currentSpeed * Time.fixedDeltaTime, remainingDistance);
        if (FishManager.Instance != null) nextPos = FishManager.Instance.ClampPosition(nextPos);

        Vector2 actualMovement = nextPos - rb.position;
        if (actualMovement.sqrMagnitude < 0.00000001f) return;
        rb.MovePosition(nextPos);
        if (visuals != null) visuals.SetFacingDirection(actualMovement.normalized);
    }

    private bool FindOrKeepTarget(Vector2 myPosition)
    {
        if (targetPlayer != null && targetPlayer.gameObject.activeInHierarchy &&
            (targetCore == null || (!targetCore.isDead && !targetCore.isInvisible && !targetCore.isInvincible)))
            return true;

        targetPlayer = null;
        targetRigidbody = null;
        targetCore = null;

        if (Time.time >= nextSearchTime) FindTarget(myPosition);
        return targetPlayer != null;
    }

    private bool HasTemporarilyProtectedPlayer()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].activeInHierarchy) continue;
            FishCore core = players[i].GetComponent<FishCore>();
            if (core != null && !core.isDead && core.isInvincible && !core.isInvisible)
                return true;
        }
        return false;
    }

    private void FindTarget(Vector2 myPosition)
    {
        nextSearchTime = Time.time + Mathf.Max(0.2f, searchInterval);
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");

        // 随机选取任何可追逐玩家，而不是每次锁定最近的一位。
        System.Collections.Generic.List<FishCore> candidates = new System.Collections.Generic.List<FishCore>();
        foreach (GameObject player in players)
        {
            if (player == null || !player.activeInHierarchy) continue;
            FishCore core = player.GetComponent<FishCore>();
            if (core == null || !core.isPlayer || core.isDead || core.isInvisible || core.isInvincible) continue;
            candidates.Add(core);
        }

        if (candidates.Count == 0) return;
        targetCore = candidates[Random.Range(0, candidates.Count)];
        targetPlayer = targetCore.transform;
        targetRigidbody = targetCore.GetComponent<Rigidbody2D>();
    }

    private void BeginExit(Vector2 position)
    {
        exiting = true;
        targetPlayer = null;
        targetRigidbody = null;
        targetCore = null;

        if (FishManager.Instance == null)
        {
            exitDirection = Vector2.right;
            return;
        }

        Vector2 min = FishManager.Instance.minBounds;
        Vector2 max = FishManager.Instance.maxBounds;
        float leftDistance = Mathf.Abs(position.x - min.x);
        float rightDistance = Mathf.Abs(max.x - position.x);
        // 鲨鱼只能从屏幕左右离场，绝不向上/下游出。
        exitDirection = leftDistance <= rightDistance ? Vector2.left : Vector2.right;
    }

    private void MoveOutOfCamera(Rigidbody2D rb, float speed)
    {
        Vector2 nextPos = rb.position + exitDirection * speed * Time.fixedDeltaTime;
        rb.MovePosition(nextPos);
        if (visuals != null) visuals.SetFacingDirection(exitDirection);

        if (FishManager.Instance == null)
        {
            if (Time.time >= chaseEndTime + 5f) DespawnShark();
            return;
        }

        Vector2 min = FishManager.Instance.minBounds;
        Vector2 max = FishManager.Instance.maxBounds;
        bool outside = nextPos.x < min.x - exitMargin || nextPos.x > max.x + exitMargin;
        if (outside) DespawnShark();
    }

    private void DespawnShark()
    {
        if (FishManager.Instance != null && selfCore != null) FishManager.Instance.NotifySharkDespawned(selfCore);
        LeanPool.Despawn(gameObject);
    }
}
