using UnityEngine;

public class PlayerInputMovement : MonoBehaviour, IMovement
{
    public bool IsActive { get; set; } = true;

    private Vector2 moveDirection = Vector2.zero;
    private FishVisuals visuals;
    private FishCore fishCore;
    private bool isRemote = false;

    void Awake()
    {
        visuals = GetComponent<FishVisuals>();
        fishCore = GetComponent<FishCore>();
    }

    public void Tick(Rigidbody2D rb, float currentSpeed)
    {
        if (!IsActive || rb == null) return;

        if (!isRemote)
        {
            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            moveDirection = new Vector2(x, y).normalized;
            if (fishCore != null) fishCore.SetBoosting(Input.GetKey(KeyCode.Space));
        }

        Vector2 nextPos = rb.position + moveDirection * currentSpeed * Time.fixedDeltaTime;
        if (FishManager.Instance != null) nextPos = FishManager.Instance.ClampPosition(nextPos);

        if ((nextPos - rb.position).sqrMagnitude > 0.00000001f) rb.MovePosition(nextPos);
        if (moveDirection.sqrMagnitude > 0.01f && visuals != null) visuals.SetFacingDirection(moveDirection);
    }

    public void SetRemoteMode(bool remote)
    {
        isRemote = remote;
        moveDirection = Vector2.zero;
        if (fishCore != null) fishCore.SetBoosting(false);
    }

    public void SetRemoteBoost(bool pressed)
    {
        if (isRemote && fishCore != null) fishCore.SetBoosting(pressed);
    }

    public void SetRemoteJoystick(int angle, float force, bool active)
    {
        if (!active || force <= 0f)
        {
            moveDirection = Vector2.zero;
            return;
        }

        float safeForce = Mathf.Clamp01(force);
        float radians = Mathf.Repeat(angle, 360f) * Mathf.Deg2Rad;

        Vector2 dir = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)).normalized;
        moveDirection = dir * safeForce;
    }
}