using Fusion;
using UnityEngine;

// Prefab enemy cần có: NetworkObject, NetworkTransform, CharacterController, EnemyController (+ Animator ở object con).
// KHÔNG thêm NetworkMecanimAnimator (script này tự đồng bộ animation).
[RequireComponent(typeof(CharacterController))]
public class EnemyController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [Tooltip("Để trống thì script tự tìm player gần nhất có tag 'Player'.")]
    [SerializeField] private Transform target;

    [Header("Detection")]
    [SerializeField] private float detectRange = 12f;
    [Tooltip("Nên lớn hơn Detect Range để enemy không chập chờn đuổi/dừng.")]
    [SerializeField] private float loseRange = 18f;
    [Tooltip("Bao lâu thì tìm lại player gần nhất (giây).")]
    [SerializeField] private float retargetInterval = 0.5f;

    [Header("Movement")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float rotationSpeed = 8f;
    [SerializeField] private float stopDistance = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Attack")]
    [Tooltip("Đặt 0 để tắt tấn công. Nên lớn hơn Stop Distance một chút.")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 0.5f;
    [Tooltip("Thời gian đứng yên khi đánh, bằng độ dài clip tấn công (giây).")]
    [SerializeField] private float attackDuration = 1f;
    [SerializeField] private bool rotateWhileAttacking = false;

    [Header("Animator Parameters")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string isChasingParameter = "IsChasing";
    [SerializeField] private string attackParameter = "Attack";
    [SerializeField] private string dieParameter = "Die";

    [Header("Debug")]
    [SerializeField] private bool logAnimatorParameters = true;

    // ---------------------------------------------------------------- Networked state
    // Chỉ máy có State Authority ghi, mọi máy đọc để chạy animation.
    [Networked] public NetworkBool IsChasing { get; set; }
    [Networked] public NetworkBool IsMoving { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }
    [Networked] public int AttackCount { get; set; }

    private struct AnimParam
    {
        public int hash;
        public bool exists;
        public AnimatorControllerParameterType type;
    }

    private CharacterController controller;
    private Vector3 verticalVelocity;
    private bool ready;

    // Chỉ dùng trên máy authority
    private bool isAttacking;
    private float attackEndTime;
    private float nextAttackTime;
    private float targetSearchTimer;

    // Chỉ dùng cho hiển thị (mọi máy)
    private int shownAttackCount;
    private bool deathShown;

    private AnimParam speedP, chasingP, attackP, dieP;

    // ---------------------------------------------------------------- Setup

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("EnemyController: Không tìm thấy Animator trong enemy.", this);
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("EnemyController: Animator chưa được gán Controller!", this);
            return;
        }

        animator.applyRootMotion = false;

        speedP = FindParam(speedParameter);
        chasingP = FindParam(isChasingParameter);
        attackP = FindParam(attackParameter);
        dieP = FindParam(dieParameter);

        if (logAnimatorParameters)
        {
            CheckParam(speedP, speedParameter, AnimatorControllerParameterType.Float);
            CheckParam(chasingP, isChasingParameter, AnimatorControllerParameterType.Bool);
            CheckParam(attackP, attackParameter, AnimatorControllerParameterType.Trigger);
            CheckParam(dieP, dieParameter, AnimatorControllerParameterType.Trigger);
        }

        ready = true;
    }

    public override void Spawned()
    {
        // Người vào sau không bị phát lại animation đánh cũ.
        shownAttackCount = AttackCount;

        // Nếu vào phòng khi enemy đã chết thì hiện luôn trạng thái chết.
        if (IsDead)
            ApplyDeathVisual();

        UpdateControllerAuthority();
    }

    // ---------------------------------------------------------------- Logic (chỉ máy authority)

    public override void FixedUpdateNetwork()
    {
        if (!ready) return;

        // Quyền có thể chuyển sang máy khác nếu người giữ quyền thoát phòng.
        UpdateControllerAuthority();

        if (!HasStateAuthority) return;
        if (IsDead) return;

        float dt = Runner.DeltaTime;
        float now = Runner.SimulationTime;

        // Tìm player gần nhất định kỳ (hoặc ngay khi mất target).
        targetSearchTimer -= dt;
        if (target == null || targetSearchTimer <= 0f)
        {
            targetSearchTimer = retargetInterval;
            FindNearestTarget();
        }

        Vector3 horizontalMove = Vector3.zero;
        bool moving = false;

        if (target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            UpdateChaseState(distance);

            if (isAttacking && now >= attackEndTime)
                isAttacking = false;

            if (IsChasing && distance > 0.001f)
            {
                Vector3 direction = toTarget / distance;

                if (!isAttacking || rotateWhileAttacking)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, targetRotation, rotationSpeed * dt);
                }

                if (!isAttacking)
                    TryAttack(distance, now);

                if (!isAttacking && distance > stopDistance)
                {
                    horizontalMove = direction * chaseSpeed;
                    moving = true;
                }
            }
        }
        else
        {
            IsChasing = false;
        }

        // Trọng lực + di chuyển
        if (controller.isGrounded && verticalVelocity.y < 0f)
            verticalVelocity.y = -2f;

        verticalVelocity.y += gravity * dt;

        Vector3 movement = horizontalMove;
        movement.y = verticalVelocity.y;
        controller.Move(movement * dt);

        IsMoving = moving;
    }

    private void UpdateChaseState(float distance)
    {
        if (!IsChasing && distance <= detectRange)
            IsChasing = true;
        else if (IsChasing && distance > loseRange)
            IsChasing = false;
    }

    private void TryAttack(float distance, float now)
    {
        if (attackRange <= 0f || distance > attackRange) return;
        if (now < nextAttackTime) return;

        isAttacking = true;
        attackEndTime = now + Mathf.Max(0.05f, attackDuration);
        nextAttackTime = attackEndTime + Mathf.Max(0f, attackCooldown);

        // Tăng biến này -> mọi máy thấy thay đổi và chạy animation đánh.
        AttackCount++;

        // TODO: gây sát thương cho player ở đây.
        // Vì player là object của máy khác, nên gửi RPC tới player đó để trừ máu
        // (ví dụ: targetPlayer.RPC_TakeDamage(10)).
        Debug.Log(name + " tấn công player!");
    }

    private void FindNearestTarget()
    {
        GameObject[] players;
        try
        {
            players = GameObject.FindGameObjectsWithTag("Player");
        }
        catch (UnityException)
        {
            return; // Tag 'Player' chưa tồn tại trong project.
        }

        Transform nearest = null;
        float best = float.MaxValue;

        foreach (GameObject p in players)
        {
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = p.transform;
            }
        }

        target = nearest;
    }

    // Bật CharacterController chỉ trên máy authority.
    // Máy khác để NetworkTransform tự đặt vị trí, tránh CharacterController giành quyền.
    private void UpdateControllerAuthority()
    {
        bool shouldEnable = HasStateAuthority && !IsDead;
        if (controller.enabled != shouldEnable)
            controller.enabled = shouldEnable;
    }

    // ---------------------------------------------------------------- Chết

    // Gọi từ bất kỳ máy nào (ví dụ khi đạn/đòn đánh của player trúng enemy).
    public void Die()
    {
        if (HasStateAuthority)
            DoDie();
        else
            RPC_Die();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Die()
    {
        DoDie();
    }

    private void DoDie()
    {
        if (IsDead) return;

        IsDead = true;
        IsChasing = false;
        IsMoving = false;
        isAttacking = false;
        controller.enabled = false;
    }

    // ---------------------------------------------------------------- Hiển thị (mọi máy)

    public override void Render()
    {
        if (!ready) return;

        // Animation chết
        if (IsDead)
        {
            if (!deathShown)
                ApplyDeathVisual();
            return;
        }

        // Animation đánh: so sánh số lần đánh đã hiện với số lần trên mạng.
        if (AttackCount != shownAttackCount)
        {
            shownAttackCount = AttackCount;
            FireTriggerP(attackP);
        }

        SetFloatP(speedP, IsMoving ? 1f : 0f);
        SetBoolP(chasingP, IsChasing);
    }

    private void ApplyDeathVisual()
    {
        deathShown = true;
        FireTriggerP(dieP);
        SetFloatP(speedP, 0f, false);
        SetBoolP(chasingP, false);
    }

    // ---------------------------------------------------------------- Gizmos

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, loseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
    }

    // ---------------------------------------------------------------- Animator helpers

    private AnimParam FindParam(string parameterName)
    {
        AnimParam p = new AnimParam();
        p.hash = Animator.StringToHash(parameterName);
        p.exists = false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName)
            {
                p.exists = true;
                p.type = parameter.type;
                break;
            }
        }

        return p;
    }

    private void CheckParam(AnimParam p, string parameterName, AnimatorControllerParameterType expected)
    {
        if (!p.exists)
            Debug.LogWarning("Animator của enemy đang THIẾU parameter: '" + parameterName +
                             "' (cần kiểu " + expected + ")", this);
        else if (p.type != expected)
            Debug.LogWarning("Parameter '" + parameterName + "' đang là kiểu " + p.type +
                             " nhưng nên là " + expected + ".", this);
    }

    private void SetFloatP(AnimParam p, float value, bool damp = true)
    {
        if (!p.exists) return;

        switch (p.type)
        {
            case AnimatorControllerParameterType.Float:
                if (damp) animator.SetFloat(p.hash, value, 0.1f, Time.deltaTime);
                else animator.SetFloat(p.hash, value);
                break;
            case AnimatorControllerParameterType.Int:
                animator.SetInteger(p.hash, Mathf.RoundToInt(value));
                break;
            case AnimatorControllerParameterType.Bool:
                animator.SetBool(p.hash, value > 0.5f);
                break;
        }
    }

    private void SetBoolP(AnimParam p, bool value)
    {
        if (!p.exists) return;

        switch (p.type)
        {
            case AnimatorControllerParameterType.Bool:
                animator.SetBool(p.hash, value);
                break;
            case AnimatorControllerParameterType.Float:
                animator.SetFloat(p.hash, value ? 1f : 0f);
                break;
            case AnimatorControllerParameterType.Int:
                animator.SetInteger(p.hash, value ? 1 : 0);
                break;
        }
    }

    private void FireTriggerP(AnimParam p)
    {
        if (!p.exists || p.type != AnimatorControllerParameterType.Trigger)
            return;

        animator.ResetTrigger(p.hash);
        animator.SetTrigger(p.hash);
    }
}