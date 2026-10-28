using UnityEngine;

/// <summary>
/// AI 태엽 장난감. Idle / Run / Prone / Jump(2또는3회) 랜덤.
/// 항상 실제 지면(길/인도)에 붙습니다.
/// </summary>
public class TickToyAI : MonoBehaviour
{
    public enum AiState
    {
        Idle,
        Run,
        Prone,
        Jump
    }

    [SerializeField] private float walkSpeed = 2.8f;
    [SerializeField] private float runSpeed = 4.5f;
    [SerializeField] private float idleMin = 1.5f;
    [SerializeField] private float idleMax = 4f;
    [SerializeField] private float runMin = 3f;
    [SerializeField] private float runMax = 8f;
    [SerializeField] private float proneMin = 2f;
    [SerializeField] private float proneMax = 5f;
    [SerializeField] private float jumpInterval = 0.35f;
    [SerializeField] private int hp = 100;

    // TicktoyMove 의 JumpPower(5.5)와 동일 — TickToy 쪽은 수정하지 않음
    const float TickToyJumpPower = 5.5f;

    Rigidbody rb;
    Collider bodyCollider;
    Animator animator;
    AiState state = AiState.Idle;
    float stateTimer;
    int jumpsLeft;
    float jumpTimer;
    bool dead;
    bool settled;
    float settleTimer = 0.15f;
    Vector3 moveDir = Vector3.forward;
    float currentSpeed;
    float lockedGroundY;
    bool jumpInAir;
    float jumpElapsed;
    float jumpDuration;
    float jumpPeakHeight;
    bool startMovingNow;

    public int Hp => hp;
    public bool IsDead => dead;

    /// <summary>분신 스폰 직후 대기 없이 바로 Run.</summary>
    public void BeginMovingNow()
    {
        startMovingNow = true;
        dead = false;
        ForceToGround(true);
        settled = true;
        settleTimer = 0f;
        EnterRun();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;

        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
            agent.enabled = false;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void Start()
    {
        ForceToGround(true);
        if (startMovingNow)
        {
            settled = true;
            settleTimer = 0f;
            if (state != AiState.Run)
                EnterRun();
            return;
        }

        settled = false;
        settleTimer = 0.15f;
        EnterIdle();
    }

    void ForceToGround(bool hard)
    {
        float preferY = transform.position.y;
        TickToyMove tickToy = FindAnyObjectByType<TickToyMove>();
        if (tickToy != null)
            preferY = tickToy.transform.position.y;
        else
        {
            WinderMove winder = FindAnyObjectByType<WinderMove>();
            if (winder != null)
                preferY = winder.transform.position.y;
        }

        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(transform.position, preferY, out Vector3 ground))
        {
            lockedGroundY = ground.y;
            Vector3 p = new Vector3(transform.position.x, ground.y, transform.position.z);
            if (rb != null)
                rb.position = p;
            transform.position = p;
            return;
        }

        if (hard)
        {
            lockedGroundY = preferY;
            Vector3 p = transform.position;
            p.y = lockedGroundY;
            if (rb != null)
                rb.position = p;
            transform.position = p;
        }
    }

    void Update()
    {
        if (dead)
            return;

        if (!settled)
        {
            settleTimer -= Time.deltaTime;
            ForceToGround(true);
            if (settleTimer <= 0f)
                settled = true;
            SetAnimMove(Vector2.zero, false);
            return;
        }

        switch (state)
        {
            case AiState.Idle:
                TickIdle();
                break;
            case AiState.Run:
                TickRun();
                break;
            case AiState.Prone:
                TickProne();
                break;
            case AiState.Jump:
                TickJump();
                break;
        }
    }

    void FixedUpdate()
    {
        if (dead || rb == null)
            return;

        SeparateFromPlayersOnly();

        if (jumpInAir)
        {
            TickJumpMotion();
            return;
        }

        // 점프 중이 아닐 때만 지면 Y 고정
        StickYToGround();

        if (!settled || state != AiState.Run || currentSpeed < 0.01f)
            return;

        Vector3 dir = moveDir;
        if (IsBlocked(dir))
        {
            if (!TrySlideDir(out dir))
            {
                currentSpeed = 0f;
                SetAnimMove(Vector2.zero, false);
                return;
            }
            moveDir = dir;
        }

        if (!HasGroundAhead(moveDir))
        {
            PickRandomMoveDir();
            return;
        }

        rb.MoveRotation(Quaternion.LookRotation(moveDir, Vector3.up));

        Vector3 next = rb.position + moveDir * (currentSpeed * Time.fixedDeltaTime);
        next.y = lockedGroundY;
        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(next, lockedGroundY, out Vector3 g))
        {
            float dy = g.y - lockedGroundY;
            if (dy > 0.4f)
                dy = 0.4f;
            if (dy < -0.4f)
                dy = -0.4f;
            lockedGroundY += dy;
            next.y = lockedGroundY;
        }

        rb.MovePosition(next);
        SetAnimMove(new Vector2(0f, 1f), false);
    }

    void TickJumpMotion()
    {
        jumpElapsed += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(jumpElapsed / jumpDuration);

        // TicktoyMove 와 같은 물리 포물선: y = v0*t - 0.5*g*t^2
        // 최고점 = JumpPower^2 / (2g)
        float height = 4f * jumpPeakHeight * t * (1f - t);

        Vector3 p = rb.position;
        p.y = lockedGroundY + height;
        rb.MovePosition(p);

        if (t >= 1f)
        {
            p.y = lockedGroundY;
            rb.MovePosition(p);
            jumpInAir = false;
            jumpTimer = jumpInterval;
        }
    }

    static float ResolveGravity()
    {
        // TicktoyMove 점프 높이 계산과 동일한 중력 처리
        float g = Mathf.Abs(Physics.gravity.y);
        return g < 0.01f ? 12f : g;
    }

    void StickYToGround()
    {
        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(rb.position, lockedGroundY != 0f ? lockedGroundY : rb.position.y, out Vector3 g))
        {
            lockedGroundY = g.y;
            Vector3 p = rb.position;
            p.y = lockedGroundY;
            rb.position = p;
        }
    }

    void SeparateFromPlayersOnly()
    {
        if (bodyCollider == null)
            return;

        TickToyMove[] players = FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            TickToyMove player = players[i];
            if (player == null)
                continue;

            Collider playerCol = player.GetComponent<Collider>();
            if (playerCol == null || !playerCol.enabled)
                continue;

            if (Physics.ComputePenetration(
                    bodyCollider, transform.position, transform.rotation,
                    playerCol, playerCol.transform.position, playerCol.transform.rotation,
                    out Vector3 direction, out float distance))
            {
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                    continue;
                direction.Normalize();
                Vector3 separated = rb.position + direction * (distance + 0.01f);
                if (!jumpInAir)
                    separated.y = lockedGroundY;
                rb.MovePosition(separated);
            }
        }
    }

    void TickIdle()
    {
        SetAnimMove(Vector2.zero, false);
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            PickNextAfterIdleOrRun();
    }

    void TickRun()
    {
        if (currentSpeed > 0.01f)
            SetAnimMove(new Vector2(0f, 1f), false);
        else
            SetAnimMove(Vector2.zero, false);

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            PickNextAfterIdleOrRun();
    }

    void TickProne()
    {
        SetAnimMove(Vector2.zero, true);
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            EnterIdle();
    }

    void TickJump()
    {
        SetAnimMove(Vector2.zero, false);
        if (jumpInAir)
            return;

        jumpTimer -= Time.deltaTime;
        if (jumpTimer > 0f)
            return;

        if (jumpsLeft <= 0)
        {
            EnterIdle();
            return;
        }

        StartHop();
    }

    void StartHop()
    {
        StickYToGround();

        float g = ResolveGravity();
        // TicktoyMove: jumpHeight = (JumpPower * JumpPower) / (2f * gravity)
        jumpPeakHeight = (TickToyJumpPower * TickToyJumpPower) / (2f * g);
        jumpDuration = (2f * TickToyJumpPower) / g;
        jumpElapsed = 0f;
        jumpInAir = true;
        jumpsLeft--;

        if (animator != null)
            animator.SetTrigger("Jump");
    }

    void PickNextAfterIdleOrRun()
    {
        float roll = Random.value;
        if (state == AiState.Idle)
        {
            EnterRun();
            return;
        }

        if (roll < 0.34f)
            EnterIdle();
        else if (roll < 0.67f)
            EnterProne();
        else
            EnterJump();
    }

    void EnterIdle()
    {
        state = AiState.Idle;
        stateTimer = Random.Range(idleMin, idleMax);
        currentSpeed = 0f;
        SetAnimMove(Vector2.zero, false);
    }

    void EnterRun()
    {
        state = AiState.Run;
        stateTimer = Random.Range(runMin, runMax);
        currentSpeed = Random.value < 0.35f ? runSpeed : walkSpeed;
        PickRandomMoveDir();
        for (int i = 0; i < 8 && (IsBlocked(moveDir) || !HasGroundAhead(moveDir)); i++)
            PickRandomMoveDir();

        if (rb != null)
            rb.MoveRotation(Quaternion.LookRotation(moveDir, Vector3.up));

        SetAnimMove(new Vector2(0f, 1f), false);
    }

    void EnterProne()
    {
        state = AiState.Prone;
        stateTimer = Random.Range(proneMin, proneMax);
        currentSpeed = 0f;
        SetAnimMove(Vector2.zero, true);
    }

    void EnterJump()
    {
        state = AiState.Jump;
        jumpsLeft = Random.value < 0.5f ? 2 : 3;
        jumpTimer = 0f;
        jumpInAir = false;
        currentSpeed = 0f;
        SetAnimMove(Vector2.zero, false);
        StartHop();
    }

    void PickRandomMoveDir()
    {
        moveDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
    }

    bool TrySlideDir(out Vector3 dir)
    {
        Vector3 right = Vector3.Cross(Vector3.up, moveDir).normalized;
        if (!IsBlocked(right) && HasGroundAhead(right))
        {
            dir = right;
            return true;
        }

        if (!IsBlocked(-right) && HasGroundAhead(-right))
        {
            dir = -right;
            return true;
        }

        dir = moveDir;
        return false;
    }

    bool IsBlocked(Vector3 dir)
    {
        Vector3 origin = transform.position + Vector3.up * 0.55f;
        if (!Physics.SphereCast(origin, 0.25f, dir, out RaycastHit hit, 0.55f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
            return false;
        return true;
    }

    bool HasGroundAhead(Vector3 dir)
    {
        Vector3 probe = transform.position + dir * 0.85f;
        probe.y = lockedGroundY + 1f;
        return AiGroundUtil.TryFindGroundNearPlayerOrWorld(probe, lockedGroundY, out _);
    }

    void SetAnimMove(Vector2 move, bool prone)
    {
        if (animator == null)
            return;

        bool moving = move.sqrMagnitude > 0.01f && !prone;
        animator.SetBool("IsProne", prone);
        animator.SetBool("IsWinding", false);
        animator.SetInteger("State", moving ? 1 : 0);
        animator.SetFloat("MoveX", move.x);
        animator.SetFloat("MoveY", move.y);
    }

    public void TakeHit(int damage)
    {
        if (dead)
            return;

        hp = Mathf.Max(0, hp - Mathf.Max(0, damage));
        if (animator != null)
            animator.SetTrigger("GetHit");

        if (hp <= 0)
            Die();
    }

    void Die()
    {
        dead = true;
        hp = 0;
        currentSpeed = 0f;
        if (animator != null)
            animator.SetTrigger("Die");
    }
}
