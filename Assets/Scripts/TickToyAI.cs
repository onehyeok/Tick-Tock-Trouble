using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AI 태엽 장난감. Idle / Run / Prone / Jump(2또는3회)를 랜덤으로 이어 갑니다.
/// 태엽 감기·E·능력은 하지 않습니다.
/// NavMesh가 있으면 Agent로, 없으면 Rigidbody로 이동합니다.
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

    [SerializeField] private float walkSpeed = 2.4f;
    [SerializeField] private float runSpeed = 4.2f;
    [SerializeField] private float idleMin = 1.5f;
    [SerializeField] private float idleMax = 4f;
    [SerializeField] private float runMin = 3f;
    [SerializeField] private float runMax = 8f;
    [SerializeField] private float proneMin = 2f;
    [SerializeField] private float proneMax = 5f;
    [SerializeField] private float roamRadius = 18f;
    [SerializeField] private float jumpInterval = 0.45f;
    [SerializeField] private int hp = 100;

    NavMeshAgent agent;
    Rigidbody rb;
    Animator animator;
    AiState state = AiState.Idle;
    float stateTimer;
    int jumpsLeft;
    float jumpTimer;
    bool dead;
    bool useAgent;
    Vector3 moveDir = Vector3.forward;
    float currentSpeed;

    public int Hp => hp;
    public bool IsDead => dead;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;

        useAgent = agent != null && agent.enabled;
        if (useAgent)
        {
            agent.speed = walkSpeed;
            agent.angularSpeed = 360f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.35f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        }

        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            if (useAgent)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            else
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }
        }
    }

    void Start()
    {
        if (useAgent && agent != null && !agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                // NavMesh 밖이면 Rigidbody 모드로 전환
                useAgent = false;
                agent.enabled = false;
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }
            }
        }

        EnterIdle();
    }

    void Update()
    {
        if (dead)
            return;

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
        if (dead || useAgent || rb == null || state != AiState.Run)
            return;

        Vector3 vel = moveDir * currentSpeed;
        vel.y = rb.linearVelocity.y;
        rb.linearVelocity = vel;

        if (moveDir.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(moveDir, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, look, 10f * Time.fixedDeltaTime));
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
        if (useAgent && agent != null)
        {
            Vector3 flatVel = agent.velocity;
            flatVel.y = 0f;
            if (flatVel.sqrMagnitude > 0.05f)
            {
                Vector3 local = transform.InverseTransformDirection(flatVel.normalized);
                SetAnimMove(new Vector2(local.x, local.z), false);
            }
            else
            {
                SetAnimMove(new Vector2(0f, 1f), false);
            }

            stateTimer -= Time.deltaTime;
            bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f;
            if (stateTimer <= 0f || arrived)
                PickNextAfterIdleOrRun();
            return;
        }

        // Rigidbody 주행: 벽/낭떠러지 앞이면 방향 전환
        SetAnimMove(new Vector2(0f, 1f), false);
        if (Physics.Raycast(transform.position + Vector3.up * 0.4f, moveDir, 1.2f, ~0, QueryTriggerInteraction.Ignore)
            || !Physics.Raycast(transform.position + moveDir * 1.1f + Vector3.up * 1.5f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            PickRandomMoveDir();
        }

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
        jumpTimer -= Time.deltaTime;
        if (jumpTimer > 0f)
            return;

        if (jumpsLeft <= 0)
        {
            EnterIdle();
            return;
        }

        if (animator != null)
            animator.SetTrigger("Jump");
        if (!useAgent && rb != null && IsGrounded())
            rb.AddForce(Vector3.up * 4.5f, ForceMode.VelocityChange);

        jumpsLeft--;
        jumpTimer = jumpInterval;
    }

    bool IsGrounded()
    {
        return Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.5f, ~0, QueryTriggerInteraction.Ignore);
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
        StopMovement();
        SetAnimMove(Vector2.zero, false);
    }

    void EnterRun()
    {
        state = AiState.Run;
        stateTimer = Random.Range(runMin, runMax);
        currentSpeed = Random.value < 0.35f ? runSpeed : walkSpeed;

        if (useAgent && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = currentSpeed;
            if (TryPickDestination(out Vector3 dest))
                agent.SetDestination(dest);
        }
        else
        {
            PickRandomMoveDir();
        }

        SetAnimMove(new Vector2(0f, 1f), false);
    }

    void EnterProne()
    {
        state = AiState.Prone;
        stateTimer = Random.Range(proneMin, proneMax);
        currentSpeed = 0f;
        StopMovement();
        SetAnimMove(Vector2.zero, true);
    }

    void EnterJump()
    {
        state = AiState.Jump;
        jumpsLeft = Random.value < 0.5f ? 2 : 3;
        jumpTimer = 0f;
        currentSpeed = 0f;
        StopMovement();
        SetAnimMove(Vector2.zero, false);
    }

    void StopMovement()
    {
        if (useAgent && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (rb != null && !rb.isKinematic)
        {
            Vector3 v = rb.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            rb.linearVelocity = v;
        }
    }

    void PickRandomMoveDir()
    {
        float yaw = Random.Range(0f, 360f);
        moveDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    }

    bool TryPickDestination(out Vector3 dest)
    {
        dest = transform.position;
        for (int i = 0; i < 12; i++)
        {
            Vector2 circle = Random.insideUnitCircle * roamRadius;
            Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                dest = hit.position;
                return true;
            }
        }

        return false;
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
        StopMovement();
        if (agent != null)
            agent.enabled = false;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (animator != null)
            animator.SetTrigger("Die");
    }
}
