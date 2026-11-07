using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [Tooltip("Mục tiêu để đuổi theo. Để trống thì script tự tìm GameObject có tag 'Player'.")]
    [SerializeField] private Transform target;

    [Header("Detection (phát hiện player)")]
    [Tooltip("Player lại gần trong khoảng cách này thì enemy bắt đầu đuổi.")]
    [SerializeField] private float detectRange = 12f;
    [Tooltip("Đang đuổi mà player chạy xa hơn khoảng cách này thì enemy bỏ cuộc và đứng lại. " +
             "Nên lớn hơn Detect Range để enemy không bị chập chờn đuổi/dừng liên tục.")]
    [SerializeField] private float loseRange = 18f;

    [Header("Movement")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float rotationSpeed = 8f;
    [Tooltip("Dừng lại khi cách player khoảng này (để không đâm vào người player).")]
    [SerializeField] private float stopDistance = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Attack (tuỳ chọn)")]
    [Tooltip("Khoảng cách để kích hoạt animation tấn công. Đặt 0 để tắt. " +
             "Nên lớn hơn Stop Distance một chút (ví dụ 2).")]
    [SerializeField] private float attackRange = 2f;
    [Tooltip("Thời gian nghỉ SAU KHI đánh xong rồi mới được đánh tiếp (giây).")]
    [SerializeField] private float attackCooldown = 0.5f;
    [Tooltip("Thời gian enemy đứng yên khi tấn công, bằng độ dài clip tấn công (giây). " +
             "Hết thời gian này enemy mới được di chuyển tiếp. " +
             "Xem độ dài clip: chọn clip trong Project, thanh thời gian ở khung Preview.")]
    [SerializeField] private float attackDuration = 1f;
    [Tooltip("Bật: enemy vẫn xoay theo player khi đang đánh. Tắt: đứng yên hoàn toàn đến hết animation.")]
    [SerializeField] private bool rotateWhileAttacking = false;

    [Header("Animator Parameters")]
    [Tooltip("Float 0..1: 0 = đứng (Idle), 1 = đang chạy đuổi. Dùng để chuyển Idle <-> Run/Walk.")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Bool (tuỳ chọn): true khi đang đuổi theo player.")]
    [SerializeField] private string isChasingParameter = "IsChasing";
    [Tooltip("Trigger (tuỳ chọn): kích hoạt khi tấn công.")]
    [SerializeField] private string attackParameter = "Attack";
    [Tooltip("Trigger (tuỳ chọn): kích hoạt khi chết.")]
    [SerializeField] private string dieParameter = "Die";

    [Header("Debug")]
    [SerializeField] private bool logAnimatorParameters = true;

    private struct AnimParam
    {
        public int hash;
        public bool exists;
        public AnimatorControllerParameterType type;
    }

    private CharacterController controller;
    private Vector3 verticalVelocity;

    private bool isChasing;
    private bool isDead;
    private bool isAttacking;     // đang trong animation tấn công, đứng yên không di chuyển
    private float attackEndTime;  // thời điểm animation tấn công kết thúc
    private float nextAttackTime;
    private float targetSearchTimer;

    private AnimParam speedP, chasingP, attackP, dieP;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("EnemyController: Không tìm thấy Animator trong enemy.", this);
            enabled = false;
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("EnemyController: Animator chưa được gán Controller!", this);
            enabled = false;
            return;
        }

        // Enemy di chuyển bằng CharacterController nên không dùng Root Motion.
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
    }

    private void Start()
    {
        FindTarget();

        if (target == null)
            Debug.LogWarning("EnemyController: Chưa có mục tiêu. Hãy gán Target hoặc gắn tag 'Player' cho nhân vật.", this);
    }

    private void Update()
    {
        if (isDead)
            return;

        // Nếu chưa có target (ví dụ player spawn sau) thì thử tìm lại mỗi 1 giây.
        if (target == null)
        {
            targetSearchTimer -= Time.deltaTime;
            if (targetSearchTimer <= 0f)
            {
                targetSearchTimer = 1f;
                FindTarget();
            }
        }

        Vector3 horizontalMove = Vector3.zero;
        bool isMoving = false;

        if (target != null)
        {
            // Khoảng cách và hướng tới player, chỉ tính trên mặt phẳng ngang.
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            UpdateChaseState(distance);

            // Hết thời gian animation tấn công thì mới cho di chuyển tiếp.
            if (isAttacking && Time.time >= attackEndTime)
                isAttacking = false;

            if (isChasing && distance > 0.001f)
            {
                Vector3 direction = toTarget / distance;

                // Xoay mặt về phía player (khi đang đánh thì chỉ xoay nếu bật rotateWhileAttacking).
                if (!isAttacking || rotateWhileAttacking)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        targetRotation,
                        rotationSpeed * Time.deltaTime);
                }

                // Thử đánh trước: nếu bắt đầu đánh thì đứng lại ngay từ frame này.
                if (!isAttacking)
                    TryAttack(distance);

                // Chưa tới gần thì chạy tới, tới gần rồi hoặc đang đánh thì đứng lại.
                if (!isAttacking && distance > stopDistance)
                {
                    horizontalMove = direction * chaseSpeed;
                    isMoving = true;
                }
            }
        }
        else
        {
            isChasing = false;
        }

        // Trọng lực.
        if (controller.isGrounded && verticalVelocity.y < 0f)
            verticalVelocity.y = -2f;

        verticalVelocity.y += gravity * Time.deltaTime;

        Vector3 movement = horizontalMove;
        movement.y = verticalVelocity.y;
        controller.Move(movement * Time.deltaTime);

        // Cập nhật Animator.
        SetFloatP(speedP, isMoving ? 1f : 0f);
        SetBoolP(chasingP, isChasing);
    }

    // Có hysteresis: vào tầm Detect thì đuổi, ra khỏi tầm Lose mới bỏ.
    private void UpdateChaseState(float distance)
    {
        if (!isChasing && distance <= detectRange)
            isChasing = true;
        else if (isChasing && distance > loseRange)
            isChasing = false;
    }

    private void TryAttack(float distance)
    {
        if (attackRange <= 0f || distance > attackRange)
            return;

        if (Time.time < nextAttackTime)
            return;

        // Bắt đầu đánh: đứng yên đến hết animation, cooldown tính từ lúc đánh xong.
        isAttacking = true;
        attackEndTime = Time.time + Mathf.Max(0.05f, attackDuration);
        nextAttackTime = attackEndTime + Mathf.Max(0f, attackCooldown);

        FireTriggerP(attackP);

        // TODO: gây sát thương cho player ở đây (gọi hàm trừ máu của player).
        Debug.Log(name + " tấn công player!");
    }

    private void FindTarget()
    {
        if (target != null)
            return;

        GameObject player = null;

        try
        {
            player = GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            // Tag 'Player' chưa tồn tại trong project, bỏ qua.
        }

        if (player != null)
            target = player.transform;
    }

    // Gọi hàm này từ hệ thống máu khi HP của enemy về 0.
    public void Die()
    {
        if (isDead)
            return;

        isDead = true;
        isChasing = false;
        isAttacking = false;

        FireTriggerP(dieP);
        SetFloatP(speedP, 0f, false);
        SetBoolP(chasingP, false);

        // Tắt CharacterController để xác không chặn đường player.
        controller.enabled = false;
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

    // Float: chấp nhận Float / Int / Bool.
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

    // Bool: chấp nhận Bool / Float / Int.
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

    // Trigger: bắt buộc phải là kiểu Trigger trong Animator.
    private void FireTriggerP(AnimParam p)
    {
        if (!p.exists || p.type != AnimatorControllerParameterType.Trigger)
            return;

        animator.ResetTrigger(p.hash);
        animator.SetTrigger(p.hash);
    }
}