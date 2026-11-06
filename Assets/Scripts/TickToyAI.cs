using System.Collections.Generic;
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

    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float idleMin = 1.5f;
    [SerializeField] private float idleMax = 4f;
    [SerializeField] private float runMin = 3f;
    [SerializeField] private float runMax = 8f;
    [SerializeField] private float proneMin = 2f;
    [SerializeField] private float proneMax = 5f;
    [SerializeField] private float jumpInterval = 0.35f;
    [SerializeField] private int hp = 100;

    // TicktoyMove 의 JumpPower(5.5)와 동일 — TickToy 쪽은 수정하지 않음
    const float TickToyJumpPower = 5f;

    Rigidbody rb;
    Collider bodyCollider;
    Animator animator;
    AiState state = AiState.Idle;
    float stateTimer;
    int jumpsLeft;
    float jumpTimer;
    bool dead;
    bool deadFalling;
    float deadFallVel;
    bool deadModelCached;
    Vector3 deadModelRestLocal;
    Quaternion deadModelRestLocalRot = Quaternion.identity;
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
    float stuckTimer;
    Vector3 lastProgressPos;
    static readonly Collider[] OverlapBuf = new Collider[24];

    // 한 물리 스텝 동안 위치를 한 번만 옮기기 위한 누적 위치 (여러 번 MovePosition → 서로 덮어써서 튀던 문제 방지)
    bool inStep;
    Vector3 stepPos;
    const float MaxPushPerStep = 0.08f;   // 겹침 밀어내기 한 스텝 최대 (순간이동 방지)
    const float MaxGroundStepY = 0.4f;    // 바닥 높이 한 스텝 최대 변화

    // 밀어낼 플레이어 몸통: 내 틱토이 + (온라인) 다른 사람 틱토이 복사본
    static readonly List<Collider> cachedPlayers = new List<Collider>();
    static float playersCachedAt = -10f;

    Vector3 CurPos => inStep ? stepPos : (rb != null ? rb.position : transform.position);

    void SetPos(Vector3 p)
    {
        if (inStep)
        {
            stepPos = p;
            return;
        }
        if (rb != null)
            rb.MovePosition(p);
        else
            transform.position = p;
    }

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
        bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;

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
        UpdateHitFace();
        if (dead)
            return;

        if (!settled)
        {
            settleTimer -= Time.deltaTime;
            ForceToGround(true);
            if (settleTimer <= 0f)
            {
                settled = true;
                ResolveWorldPenetration();
                lastProgressPos = transform.position;
            }
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
        if (rb == null)
            return;

        if (dead)
        {
            if (deadFalling)
                TickDeadFall();
            else
                TickDeadSettled();
            return;
        }

        inStep = true;
        stepPos = rb.position;
        StepMove();
        inStep = false;
        if ((stepPos - rb.position).sqrMagnitude > 0.0000001f)
            rb.MovePosition(stepPos);
    }

    void StepMove()
    {
        SeparateFromPlayersOnly();

        if (jumpInAir)
        {
            TickJumpMotion();
            return;
        }

        StickYToGround();
        ResolveWorldPenetration();

        if (!settled || state != AiState.Run)
            return;

        // ???? ?? 0?? ??? ?? ? ??? ??
        if (currentSpeed < 0.01f)
            currentSpeed = walkSpeed;

        Vector3 dir = moveDir;
        if (IsBlocked(dir))
        {
            if (!TrySlideDir(out dir))
            {
                EscapeStuck();
                return;
            }
            moveDir = dir;
        }

        if (!HasGroundAhead(moveDir))
        {
            PickClearMoveDir();
            return;
        }

        rb.MoveRotation(Quaternion.LookRotation(moveDir, Vector3.up));

        Vector3 next = CurPos + moveDir * (currentSpeed * Time.fixedDeltaTime);
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

        // ?? ?? ??? ?? ? ???? ? ?
        if (WouldOverlapAt(next))
        {
            EscapeStuck();
            return;
        }

        SetPos(next);
        SetAnimMove(new Vector2(0f, 1f), false);
        TrackProgress(next);
    }

    void TickJumpMotion()
    {
        jumpElapsed += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(jumpElapsed / jumpDuration);

        // TicktoyMove 와 같은 물리 포물선: y = v0*t - 0.5*g*t^2
        // 최고점 = JumpPower^2 / (2g)
        float height = 4f * jumpPeakHeight * t * (1f - t);

        Vector3 p = CurPos;
        p.y = lockedGroundY + height;
        SetPos(p);

        if (t >= 1f)
        {
            p.y = lockedGroundY;
            SetPos(p);
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
        Vector3 p = CurPos;
        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(p, lockedGroundY != 0f ? lockedGroundY : p.y, out Vector3 g))
        {
            // 지붕·다른 물체 위로 갑자기 붙어 사라지는 것 방지: 한 스텝에 조금씩만
            float dy = Mathf.Clamp(g.y - lockedGroundY, -MaxGroundStepY, MaxGroundStepY);
            if (lockedGroundY == 0f)
                dy = g.y;
            lockedGroundY += dy;
            p.y = lockedGroundY;
            SetPos(p);
        }
    }

    void SeparateFromPlayersOnly()
    {
        if (bodyCollider == null)
            return;

        // 매 물리 스텝·AI마다 씬 전체 검색하던 것 → 0.5초마다 한 번만 (AI 40마리 렉 원인)
        if (Time.time - playersCachedAt > 0.5f)
        {
            cachedPlayers.Clear();
            foreach (TickToyMove m in FindObjectsByType<TickToyMove>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Collider c = m.GetComponent<Collider>();
                if (c != null)
                    cachedPlayers.Add(c);
            }
            if (FusionMatchBackend.Online != null)
            {
                foreach (NetPuppet p in FindObjectsByType<NetPuppet>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    Collider c = p.Kind == NetPose.KindTickToy ? p.GetComponent<Collider>() : null;
                    if (c != null)
                        cachedPlayers.Add(c);
                }
            }
            playersCachedAt = Time.time;
        }
        for (int i = 0; i < cachedPlayers.Count; i++)
        {
            Collider playerCol = cachedPlayers[i];
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
                Vector3 separated = CurPos + direction * Mathf.Min(distance + 0.01f, MaxPushPerStep);
                if (!jumpInAir)
                    separated.y = lockedGroundY;
                SetPos(separated);
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
        // AI 는 능력(달리기)을 못 씀 → 항상 걷기 속도
        currentSpeed = walkSpeed;
        stuckTimer = 0f;
        lastProgressPos = rb != null ? rb.position : transform.position;
        ResolveWorldPenetration();
        PickClearMoveDir();

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

    void PickClearMoveDir()
    {
        for (int i = 0; i < 16; i++)
        {
            PickRandomMoveDir();
            if (!IsBlocked(moveDir) && HasGroundAhead(moveDir))
                return;
        }

        // ?? ??? ????
        moveDir = -moveDir;
        if (moveDir.sqrMagnitude < 0.01f)
            moveDir = Vector3.forward;
        moveDir.Normalize();
    }

    void TrackProgress(Vector3 pos)
    {
        Vector3 flat = pos - lastProgressPos;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.04f)
        {
            lastProgressPos = pos;
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.fixedDeltaTime;
        if (stuckTimer >= 0.45f)
            EscapeStuck();
    }

    void EscapeStuck()
    {
        stuckTimer = 0f;
        ResolveWorldPenetration();
        // 예전: 0.6~1.6m 옆으로 순간이동(NudgeToClearSpot) → 이제 방향만 바꿔서 걸어서 빠져나감
        PickClearMoveDir();
        if (currentSpeed < walkSpeed)
            currentSpeed = walkSpeed;
        SetAnimMove(new Vector2(0f, 1f), false);
    }

    void NudgeToClearSpot()
    {
        Vector3 origin = rb != null ? rb.position : transform.position;
        for (int i = 0; i < 12; i++)
        {
            float ang = (360f / 12f) * i + Random.Range(0f, 20f);
            Vector3 candidate = origin + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * Random.Range(0.6f, 1.6f);
            if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(candidate, lockedGroundY, out Vector3 g))
                candidate = g;
            else
                candidate.y = lockedGroundY;

            if (WouldOverlapAt(candidate))
                continue;

            if (rb != null)
                rb.MovePosition(candidate);
            else
                transform.position = candidate;
            lastProgressPos = candidate;
            lockedGroundY = candidate.y;
            return;
        }
    }

    void ResolveWorldPenetration()
    {
        if (bodyCollider == null || rb == null)
            return;

        Vector3 pos = CurPos;
        GetBodyProbe(pos, out Vector3 center, out Vector3 half);

        int count = Physics.OverlapBoxNonAlloc(
            center, half, OverlapBuf, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        Vector3 push = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Collider other = OverlapBuf[i];
            if (other == null || other == bodyCollider)
                continue;
            if (other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform))
                continue;
            // ?? AI/????? SeparateFromPlayersOnly ???
            if (other.GetComponentInParent<TickToyAI>() != null)
                continue;
            if (other.GetComponentInParent<TickToyMove>() != null)
                continue;
            if (other.GetComponentInParent<WinderMove>() != null)
                continue;

            if (!Physics.ComputePenetration(
                    bodyCollider, pos, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                continue;

            // ?? ??(??) ??? ?? ? StickYToGround? ??
            if (dir.y > 0.65f)
                continue;

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                continue;
            dir.Normalize();
            push += dir * (dist + 0.03f);
        }

        if (push.sqrMagnitude < 0.00001f)
            return;

        // 한 번에 크게 밀면 순간이동처럼 보임 → 스텝마다 조금씩
        if (push.sqrMagnitude > MaxPushPerStep * MaxPushPerStep)
            push = push.normalized * MaxPushPerStep;

        Vector3 freed = pos + push;
        freed.y = lockedGroundY;
        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(freed, lockedGroundY, out Vector3 ground))
        {
            lockedGroundY = ground.y;
            freed.y = lockedGroundY;
        }

        SetPos(freed);
        lastProgressPos = freed;
    }

    void GetBodyProbe(Vector3 worldPos, out Vector3 center, out Vector3 half)
    {
        CapsuleCollider cap = bodyCollider as CapsuleCollider;
        if (cap != null)
        {
            Vector3 lossy = transform.lossyScale;
            float radius = Mathf.Max(cap.radius * Mathf.Max(lossy.x, lossy.z), 0.12f);
            float height = Mathf.Max(cap.height * lossy.y, radius * 2f);
            half = new Vector3(
                Mathf.Max(0.12f, radius * 0.75f),
                Mathf.Max(0.35f, height * 0.5f * 0.55f),
                Mathf.Max(0.12f, radius * 0.75f));
            Vector3 local = cap.center + Vector3.up * 0.17f;
            center = worldPos + transform.rotation * Vector3.Scale(local, lossy);
            center.y = worldPos.y + half.y + 0.08f;
            return;
        }

        Bounds b = bodyCollider.bounds;
        Vector3 delta = worldPos - b.center;
        delta.y = worldPos.y - (rb != null ? rb.position.y : transform.position.y);
        center = b.center + delta + Vector3.up * 0.2f;
        half = b.extents;
        half.x = Mathf.Max(0.12f, half.x * 0.75f);
        half.y = Mathf.Max(0.35f, half.y * 0.55f);
        half.z = Mathf.Max(0.12f, half.z * 0.75f);
        center.y = worldPos.y + half.y + 0.08f;
    }

    bool WouldOverlapAt(Vector3 worldPos)
    {
        if (bodyCollider == null)
            return false;

        GetBodyProbe(worldPos, out Vector3 center, out Vector3 half);

        int count = Physics.OverlapBoxNonAlloc(
            center, half, OverlapBuf, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = OverlapBuf[i];
            if (other == null || other == bodyCollider)
                continue;
            if (other.transform.IsChildOf(transform) || transform.IsChildOf(other.transform))
                continue;
            if (other.GetComponentInParent<TickToyAI>() != null)
                continue;
            if (other.GetComponentInParent<TickToyMove>() != null)
                continue;

            // ClosestPoint는 볼록하지 않은 메쉬 콜라이더(건물·길)에서 안 됨 → 경고 폭주 + 항상 '겹침'으로 오판해 순간이동 유발
            // ComputePenetration은 모든 콜라이더에서 동작
            if (!Physics.ComputePenetration(
                    bodyCollider, worldPos, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                continue;

            // 바닥(위로 밀어내는 면)은 겹침 아님
            if (dir.y > 0.65f)
                continue;
            if (dist > 0.03f)
                return true;
        }

        return false;
    }

    bool TrySlideDir(out Vector3 dir)
    {
        float[] yaws = { 55f, -55f, 90f, -90f, 125f, -125f, 160f, -160f, 180f };
        for (int i = 0; i < yaws.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, yaws[i], 0f) * moveDir;
            candidate.y = 0f;
            if (candidate.sqrMagnitude < 0.01f)
                continue;
            candidate.Normalize();
            if (!IsBlocked(candidate) && HasGroundAhead(candidate))
            {
                dir = candidate;
                return true;
            }
        }

        dir = moveDir;
        return false;
    }

    bool IsBlocked(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f)
            return true;
        dir.Normalize();

        // ?? ?? SphereCast? ?? ??? ???? ??(?? normal ??)
        Vector3 origin = transform.position + Vector3.up * 0.7f;
        float radius = 0.22f;
        float dist = 0.55f;
        if (!Physics.SphereCast(origin, radius, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
            return false;

        // ?? ? ?? ??/?? ?? ???? ?? ??
        if (hit.normal.y >= 0.45f)
            return false;
        if (hit.point.y <= lockedGroundY + 0.28f && hit.normal.y >= 0.2f)
            return false;

        return true;
    }

    bool HasGroundAhead(Vector3 dir)
    {
        Vector3 probe = transform.position + dir * 0.85f;
        probe.y = lockedGroundY + 1f;
        if (!AiGroundUtil.TryFindGroundNearPlayerOrWorld(probe, lockedGroundY, out Vector3 g))
            return false;
        // ????/???? ??
        return Mathf.Abs(g.y - lockedGroundY) <= 0.85f;
    }

    void SetAnimMove(Vector2 move, bool prone)
    {
        if (animator == null)
            return;

        // Backward 클립 미사용 (플레이어와 동일)
        if (move.y < -0.01f)
        {
            if (Mathf.Abs(move.x) >= 0.05f)
                move = new Vector2(-Mathf.Sign(move.x), 0f); // AS/DS 좌우 반전
            else
                move = new Vector2(0f, 1f);
        }

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

        HitFeedback.PlayAiHit(transform);
        ShowHitFace();

        if (hp <= 0)
            Die();
    }

    // ---------- 맞았을 때 표정: 잠깐 @@ (Face_Dizzy) → 원래 표정 ----------
    public const float HitFaceSeconds = 0.8f;
    List<Renderer> faceRenderers;
    readonly List<bool> faceWasOn = new List<bool>();
    float hitFaceUntil;

    /// <summary>방장: 다른 사람 화면에도 맞은 표정을 보이게 (NetCharacter 가 읽음)</summary>
    public bool HitFaceActive => hitFaceUntil > 0f;

    void ShowHitFace()
    {
        if (faceRenderers == null)
        {
            faceRenderers = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Face_"))
                    faceRenderers.Add(r);
            }
        }
        if (!faceRenderers.Exists(r => r != null && r.name.EndsWith("Face_Dizzy")))
            return;

        if (hitFaceUntil <= 0f)
        {
            faceWasOn.Clear();
            foreach (Renderer r in faceRenderers)
                faceWasOn.Add(r != null && r.enabled);
        }
        foreach (Renderer r in faceRenderers)
        {
            if (r != null)
                r.enabled = r.name.EndsWith("Face_Dizzy");
        }
        hitFaceUntil = Time.time + HitFaceSeconds;
    }

    void UpdateHitFace()
    {
        if (hitFaceUntil <= 0f || Time.time < hitFaceUntil)
            return;
        hitFaceUntil = 0f;
        if (dead)
            return; // 죽으면 @@ 그대로
        for (int i = 0; i < faceRenderers.Count && i < faceWasOn.Count; i++)
        {
            if (faceRenderers[i] != null)
                faceRenderers[i].enabled = faceWasOn[i];
        }
    }

    void Die()
    {
        dead = true;
        hp = 0;
        currentSpeed = 0f;

        // 점프 중이면 현재 상승/하강 속도로 자연 낙하
        if (rb != null && (jumpInAir || rb.position.y > lockedGroundY + 0.08f))
        {
            deadFallVel = jumpInAir ? EstimateJumpVerticalVelocity() : 0f;
            jumpInAir = false;
            deadFalling = true;
            if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(rb.position, lockedGroundY != 0f ? lockedGroundY : rb.position.y, out Vector3 g))
                lockedGroundY = g.y;
        }
        else
        {
            jumpInAir = false;
            deadFalling = false;
            deadFallVel = 0f;
            StickYToGround();
        }

        if (animator != null)
            animator.SetTrigger("Die");
    }

    float EstimateJumpVerticalVelocity()
    {
        if (jumpDuration < 0.01f)
            return 0f;
        float t = Mathf.Clamp01(jumpElapsed / jumpDuration);
        // height = 4*peak*t*(1-t) → vy = dheight/dt
        return (4f * jumpPeakHeight * (1f - 2f * t)) / jumpDuration;
    }

    void TickDeadFall()
    {
        float g = ResolveGravity();
        deadFallVel -= g * Time.fixedDeltaTime;

        Vector3 p = rb.position;
        if (AiGroundUtil.TryFindGroundNearPlayerOrWorld(p, lockedGroundY != 0f ? lockedGroundY : p.y, out Vector3 ground))
            lockedGroundY = ground.y;

        p.y += deadFallVel * Time.fixedDeltaTime;
        if (p.y <= lockedGroundY)
        {
            p.y = lockedGroundY;
            deadFalling = false;
            deadFallVel = 0f;
        }

        rb.MovePosition(p);
    }

    void TickDeadSettled()
    {
        if (bodyCollider != null && !bodyCollider.enabled)
            bodyCollider.enabled = true;

        LiftDownedMeshToGround();
        FitDeadCollider();
    }

    void LiftDownedMeshToGround()
    {
        if (animator == null || !animator.isHuman)
            return;

        if (!deadModelCached)
        {
            deadModelRestLocal = animator.transform.localPosition;
            deadModelRestLocalRot = animator.transform.localRotation;
            deadModelCached = true;
        }
        else
        {
            animator.transform.localPosition = deadModelRestLocal;
            animator.transform.localRotation = deadModelRestLocalRot;
        }

        LevelDeadPose();

        float contactY = float.PositiveInfinity;
        AccAiBoneY(HumanBodyBones.Hips, ref contactY);
        AccAiBoneY(HumanBodyBones.Spine, ref contactY);
        AccAiBoneY(HumanBodyBones.Chest, ref contactY);
        AccAiBoneY(HumanBodyBones.Head, ref contactY);
        AccAiBoneY(HumanBodyBones.LeftHand, ref contactY);
        AccAiBoneY(HumanBodyBones.RightHand, ref contactY);
        AccAiBoneY(HumanBodyBones.LeftFoot, ref contactY);
        AccAiBoneY(HumanBodyBones.RightFoot, ref contactY);
        AccAiBoneY(HumanBodyBones.LeftLowerLeg, ref contactY);
        AccAiBoneY(HumanBodyBones.RightLowerLeg, ref contactY);
        if (float.IsPositiveInfinity(contactY))
            return;

        const float pad = 0.14f;
        float target = lockedGroundY + pad;
        float gap = contactY - target;
        if (Mathf.Abs(gap) <= 0.015f || Mathf.Abs(gap) > 2.5f)
            return;

        animator.transform.position -= Vector3.up * gap;
    }

    void LevelDeadPose()
    {
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (head == null || hips == null || leftFoot == null || rightFoot == null)
            return;

        Vector3 feet = (leftFoot.position + rightFoot.position) * 0.5f;
        Vector3 along = head.position - feet;
        Vector3 flat = new Vector3(along.x, 0f, along.z);
        if (flat.sqrMagnitude < 0.0001f)
            return;

        Vector3 axis = Vector3.Cross(along, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            return;
        axis.Normalize();

        float angle = Vector3.SignedAngle(along, flat, axis);
        angle = Mathf.Clamp(angle, -28f, 28f);
        if (Mathf.Abs(angle) < 0.5f)
            return;

        animator.transform.RotateAround(hips.position, axis, angle);
    }

    void AccAiBoneY(HumanBodyBones bone, ref float lowestY)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null)
            return;
        if (t.position.y < lowestY)
            lowestY = t.position.y;
    }

    void FitDeadCollider()
    {
        CapsuleCollider cap = bodyCollider as CapsuleCollider;
        if (cap == null || animator == null || !animator.isHuman)
        {
            if (bodyCollider != null)
                bodyCollider.enabled = true;
            return;
        }

        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        EncapsulateAiBone(HumanBodyBones.Hips, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.Spine, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.Chest, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.Neck, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.Head, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.LeftUpperArm, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.RightUpperArm, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.LeftHand, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.RightHand, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.LeftUpperLeg, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.RightUpperLeg, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.LeftFoot, ref min, ref max);
        EncapsulateAiBone(HumanBodyBones.RightFoot, ref min, ref max);
        if (float.IsPositiveInfinity(min.x))
        {
            cap.enabled = true;
            return;
        }

        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (head != null && hips != null)
        {
            Vector3 headLocal = transform.InverseTransformPoint(head.position);
            Vector3 hipsLocal = transform.InverseTransformPoint(hips.position);
            Vector3 along = headLocal - hipsLocal;
            if (along.sqrMagnitude > 0.0001f)
            {
                Vector3 snout = headLocal + along.normalized * 0.35f;
                min = Vector3.Min(min, snout);
                max = Vector3.Max(max, snout);
            }
        }

        min -= new Vector3(0.14f, 0.04f, 0.14f);
        max += new Vector3(0.14f, 0.08f, 0.14f);

        Vector3 size = max - min;
        size.x = Mathf.Max(size.x, 0.5f);
        size.y = Mathf.Max(size.y, 0.35f);
        size.z = Mathf.Max(size.z, 0.5f);

        bool alongZ = size.z >= size.x;
        float longLen = alongZ ? size.z : size.x;
        float lateral = alongZ ? size.x : size.z;
        float radius = Mathf.Max(Mathf.Max(lateral, size.y) * 0.5f, 0.18f);

        cap.direction = alongZ ? 2 : 0;
        cap.height = Mathf.Max(longLen, radius * 2f + 0.02f);
        cap.radius = radius;
        cap.center = (min + max) * 0.5f;
        cap.enabled = true;
    }

    void EncapsulateAiBone(HumanBodyBones bone, ref Vector3 min, ref Vector3 max)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t == null)
            return;
        Vector3 local = transform.InverseTransformPoint(t.position);
        min = Vector3.Min(min, local);
        max = Vector3.Max(max, local);
    }
}
