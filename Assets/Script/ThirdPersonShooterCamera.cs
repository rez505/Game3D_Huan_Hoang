using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ThirdPersonShooterCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Camera Height")]
    public float targetHeight = 1.5f;

    [Header("Normal Camera")]
    public float normalDistance = 2.8f;
    public Vector3 normalShoulderOffset =
        new Vector3(0.55f, 0.15f, 0f);

    [Header("Aim Camera")]
    public float aimDistance = 1.6f;
    public Vector3 aimShoulderOffset =
        new Vector3(0.7f, 0.1f, 0f);

    [Header("Distance Limit")]
    [Tooltip("Camera không bao giờ xa nhân vật hơn khoảng này, kể cả khi Normal Distance " +
             "trong Inspector đặt lớn hơn.")]
    public float maxCameraDistance = 3f;

    [Header("Mouse")]
    public float mouseSensitivity = 0.12f;
    public float minPitch = -35f;
    public float maxPitch = 70f;

    [Header("Smooth")]
    public float positionSmooth = 15f;
    public float rotationSmooth = 15f;
    public float aimSmooth = 12f;

    [Header("Camera Collision (chống nhìn xuyên vật thể)")]
    [Tooltip("Các layer camera sẽ va chạm. Nên bỏ layer của Player ra (hoặc để mặc định, " +
             "script đã tự bỏ qua collider của Target).")]
    public LayerMask collisionLayers = ~0;
    [Tooltip("Bán kính 'quả cầu' camera. Nên >= 0.3 để không thấy xuyên tường ở mép màn hình.")]
    public float collisionRadius = 0.3f;
    [Tooltip("Khoảng cách chừa lại giữa camera và vật cản.")]
    public float collisionPadding = 0.1f;
    [Tooltip("Camera không bao giờ gần điểm xoay hơn khoảng này.")]
    public float minCameraDistance = 0.3f;

    [Header("Near Clip")]
    [Tooltip("Thu nhỏ Near Clip Plane để camera ít cắt vào vật thể khi sát tường.")]
    public bool adjustNearClip = true;
    public float nearClip = 0.05f;

    [Header("Cursor")]
    public bool lockCursorOnStart = true;

    private float yaw;
    private float pitch = 12f;

    private float currentDistance;
    private bool isAiming;
    private bool cursorLocked;

    private Camera cam;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError(
                "ThirdPersonShooterCamera: Missing Target!"
            );

            enabled = false;
            return;
        }

        cam = GetComponent<Camera>();
        if (cam != null && adjustNearClip)
            cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, Mathf.Max(0.01f, nearClip));

        yaw = target.eulerAngles.y;
        currentDistance = Mathf.Min(normalDistance, maxCameraDistance);

        if (lockCursorOnStart)
            SetCursorLock(true);
    }

    private void Update()
    {
        if (EscapePressed())
        {
            SetCursorLock(!cursorLocked);
        }

        if (!cursorLocked)
        {
            if (LeftMousePressed())
                SetCursorLock(true);

            return;
        }

        ReadMouse();

        isAiming = RightMousePressed();

        float targetDistance = Mathf.Min(
            isAiming ? aimDistance : normalDistance,
            maxCameraDistance
        );

        currentDistance = Mathf.Lerp(
            currentDistance,
            targetDistance,
            1f - Mathf.Exp(-aimSmooth * Time.deltaTime)
        );
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        Quaternion cameraRotation =
            Quaternion.Euler(pitch, yaw, 0f);

        Vector3 pivot =
            target.position + Vector3.up * targetHeight;

        Vector3 shoulderOffset = isAiming
            ? aimShoulderOffset
            : normalShoulderOffset;

        // Bước 1: dịch điểm xoay sang vai, nhưng không cho xuyên qua tường bên cạnh nhân vật.
        Vector3 shoulderPivot = ResolveCollision(
            pivot,
            pivot + cameraRotation * shoulderOffset
        );

        // Bước 2: lùi camera ra sau lưng, bị vật cản chặn thì kéo camera lại gần.
        Vector3 desiredPosition =
            shoulderPivot -
            cameraRotation * Vector3.forward * currentDistance;

        desiredPosition = ResolveCollision(shoulderPivot, desiredPosition);

        float positionFactor =
            1f - Mathf.Exp(
                -positionSmooth * Time.deltaTime
            );

        Vector3 smoothedPosition = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionFactor
        );

        // Bước 3: kiểm tra lại vị trí SAU khi làm mượt.
        // Nếu không, camera đang trượt (lerp) có thể chui ra sau tường trong vài khung hình.
        smoothedPosition = ResolveCollision(shoulderPivot, smoothedPosition);

        transform.position = smoothedPosition;

        // Camera always looks toward the aiming point.
        Vector3 lookPoint =
            pivot + cameraRotation * Vector3.forward * 10f;

        Quaternion desiredRotation =
            Quaternion.LookRotation(
                lookPoint - transform.position,
                Vector3.up
            );

        float rotationFactor =
            1f - Mathf.Exp(
                -rotationSmooth * Time.deltaTime
            );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationFactor
        );
    }

    // Quét một quả cầu từ 'from' tới 'to'. Nếu gặp vật cản thì trả về điểm an toàn
    // trước vật cản. Bỏ qua collider của chính Target (nhân vật).
    private Vector3 ResolveCollision(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;

        if (length < 0.001f)
            return to;

        direction /= length;

        int count = Physics.SphereCastNonAlloc(
            from,
            collisionRadius,
            direction,
            hitBuffer,
            length,
            collisionLayers,
            QueryTriggerInteraction.Ignore
        );

        float closest = length;
        bool blocked = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];

            if (hit.transform.IsChildOf(target))
                continue;

            if (hit.distance < closest)
            {
                closest = hit.distance;
                blocked = true;
            }
        }

        if (!blocked)
            return to;

        float safeDistance = Mathf.Max(
            minCameraDistance,
            closest - collisionPadding
        );

        return from + direction * Mathf.Min(safeDistance, length);
    }

    private void ReadMouse()
    {
        Vector2 mouse = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            mouse = Mouse.current.delta.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
        mouse = new Vector2(
            Input.GetAxis("Mouse X") * 15f,
            Input.GetAxis("Mouse Y") * 15f
        );
#endif

        yaw += mouse.x * mouseSensitivity;
        pitch -= mouse.y * mouseSensitivity;

        pitch = Mathf.Clamp(
            pitch,
            minPitch,
            maxPitch
        );
    }

    private bool RightMousePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    private bool LeftMousePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null &&
               Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    private void SetCursorLock(bool locked)
    {
        cursorLocked = locked;

        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }
}