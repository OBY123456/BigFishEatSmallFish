using UnityEngine;

public class AIWanderMovement : MonoBehaviour, IMovement
{
    public bool IsActive { get; set; } = true;
    private Vector2 moveDir;
    private float timer;
    private FishVisuals visuals;
    private Transform vortexTarget;
    private float vortexEndTime;

    void Awake()
    {
        visuals = GetComponent<FishVisuals>();
    }

    void OnEnable()
    {
        vortexTarget = null;
        SetRandomDir();
    }

    public void ApplyVortex(Transform target, float duration)
    {
        vortexTarget = target;
        vortexEndTime = Time.time + Mathf.Max(0.1f, duration);
    }

    public void Tick(Rigidbody2D rb, float currentSpeed)
    {
        if (!IsActive || rb == null || currentSpeed <= 0f) return;

        if (vortexTarget != null && vortexTarget.gameObject.activeInHierarchy && Time.time < vortexEndTime)
        {
            Vector2 toTarget = (Vector2)vortexTarget.position - rb.position;
            Vector2 pulledPosition = Vector2.MoveTowards(rb.position, (Vector2)vortexTarget.position,
                Mathf.Max(0.05f, currentSpeed * 1.8f) * Time.fixedDeltaTime);
            if (FishManager.Instance != null) pulledPosition = FishManager.Instance.ClampPosition(pulledPosition);
            rb.MovePosition(pulledPosition);
            if (toTarget.sqrMagnitude > 0.01f && visuals != null) visuals.SetFacingDirection(toTarget.normalized);
            return;
        }
        else vortexTarget = null;

        timer -= Time.fixedDeltaTime;
        if (timer <= 0f) SetRandomDir();

        Vector2 nextPos = rb.position + moveDir * currentSpeed * Time.fixedDeltaTime;
        if (FishManager.Instance != null)
        {
            Vector2 min = FishManager.Instance.minBounds;
            Vector2 max = FishManager.Instance.maxBounds;
            bool hitLeft = nextPos.x < min.x;
            bool hitRight = nextPos.x > max.x;
            bool hitBottom = nextPos.y < min.y;
            bool hitTop = nextPos.y > max.y;

            if (hitLeft || hitRight || hitBottom || hitTop)
            {
                nextPos = FishManager.Instance.ClampPosition(nextPos);
                if (hitLeft) moveDir.x = Mathf.Abs(moveDir.x);
                if (hitRight) moveDir.x = -Mathf.Abs(moveDir.x);
                if (hitBottom) moveDir.y = Mathf.Abs(moveDir.y);
                if (hitTop) moveDir.y = -Mathf.Abs(moveDir.y);
                moveDir.Normalize();
                timer = Random.Range(1f, 2f);
            }
        }

        rb.MovePosition(nextPos);
        if (visuals != null) visuals.SetFacingDirection(moveDir);
    }

    private void SetRandomDir()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        moveDir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;
        timer = Random.Range(2f, 5f);
    }
}
