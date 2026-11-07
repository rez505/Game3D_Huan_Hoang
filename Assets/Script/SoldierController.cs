using System.Collections;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class SoldierController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Transform muzzlePoint;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 2f;
    [Tooltip("Bật: đứng yên khi đang bắn, nhả chuột ra thì di chuyển lại bình thường.")]
    [SerializeField] private bool stopMovingWhileShooting = true;
    [Tooltip("Đứng yên thêm một chút sau mỗi phát bắn (giúp bắn lẻ cũng đứng lại).")]
    [SerializeField] private float standStillAfterShot = 0.15f;

    [Header("Animator Parameters")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private string shootParameter = "Shoot";
    [Tooltip("Parameter kiểu BOOL. Còn giữ chuột bắn thì = true, state bắn sẽ lặp (Loop) và không bị giật về Idle. " +
             "Nếu Animator chưa có parameter này thì script tự dùng Trigger 'Shoot' như cũ.")]
    [SerializeField] private string isShootingParameter = "IsShooting";
    [SerializeField] private string shootModeParameter = "ShootMode";
    [SerializeField] private string groundedParameter = "IsGrounded";
    [SerializeField] private string aimingParameter = "IsAiming";
    [SerializeField] private string moveXParameter = "MoveX";
    [SerializeField] private string moveYParameter = "MoveY";
    [SerializeField] private string reloadParameter = "Reload";
    [SerializeField] private string dieParameter = "Die";
    [SerializeField] private string jumpParameter = "Jump";

    [Header("Shooting Mode")]
    [SerializeField] private int startingShootMode = 2;
    [SerializeField] private float shootCooldown = 0.12f;
    [Tooltip("Bật: giữ chuột trái là bắn liên tục ở cả 3 chế độ (Auto/Single/Burst).")]
    [SerializeField] private bool holdToFireInAllModes = true;
    [Header("Single Mode (kiểu súng ngắn)")]
    [Tooltip("Bật: chế độ 2 là bán tự động như súng ngắn, mỗi lần BẤM chuột ra đúng 1 viên " +
             "(giữ chuột không bắn liên tục). Tắt: giữ chuột sẽ bắn lặp theo Single Shot Interval.")]
    [SerializeField] private bool singleRequiresClick = true;
    [Tooltip("Thời gian tối thiểu giữa 2 viên ở chế độ Single. Càng lớn càng chậm (súng ngắn ~0.5 - 0.7). " +
             "Nên chậm hơn Shoot Cooldown của Auto.")]
    [SerializeField] private float singleShotInterval = 0.6f;
    [Tooltip("Nếu bấm chuột sớm một chút trong lúc đang chờ giữa 2 viên, " +
             "phát bắn sẽ được nhớ và ra ngay khi hết thời gian chờ (giúp bấm không bị mất).")]
    [SerializeField] private float singleClickBuffer = 0.25f;
    [Tooltip("Bật: chế độ 2 có giới hạn đạn. Bắn hết đạn trong băng thì tự động nạp đạn, " +
             "nạp xong mới bắn tiếp được. Chế độ 1 và 3 không bị giới hạn.")]
    [SerializeField] private bool singleNeedsReload = true;
    [Tooltip("Số viên trong một băng ở chế độ Single. 1 = bắn 1 viên là phải nạp lại.")]
    [SerializeField] private int singleMagazineSize = 1;
    [Tooltip("Sau viên bắn cuối của băng, chờ bao lâu rồi mới tự nạp đạn " +
             "(để animation và tiếng bắn kịp chạy xong).")]
    [SerializeField] private float singleReloadDelay = 0.4f;

    [Tooltip("Thời gian nghỉ giữa 2 loạt bắn ở chế độ Burst khi giữ chuột.")]
    [SerializeField] private float burstRepeatDelay = 0.25f;
    [SerializeField] private int burstBulletCount = 3;
    [SerializeField] private float burstInterval = 0.12f;
    [SerializeField] private float shootRange = 100f;
    [Tooltip("Bật: đạn xuất phát từ nòng súng nhưng bay về đúng điểm ở GIỮA MÀN HÌNH (tâm ngắm). " +
             "Tắt: đạn bay thẳng theo hướng mũi tên xanh (trục Z) của Muzzle Point.")]
    [SerializeField] private bool aimAtCrosshair = true;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Raise Weapon (đưa súng lên rồi mới ra đạn)")]
    [Tooltip("Thời gian chờ animation đưa súng lên trước khi ra đạn + hiệu ứng ở phát ĐẦU TIÊN. " +
             "Chỉnh bằng thời gian từ lúc bấm bắn đến lúc súng giơ lên xong. Đặt 0 để tắt.")]
    [SerializeField] private float raiseWeaponDelay = 0.3f;
    [Tooltip("Sau phát bắn cuối cùng, quá khoảng thời gian này thì súng coi như đã hạ xuống " +
             "và lần bắn tiếp theo phải chờ đưa súng lên lại. Nên bằng thời gian animation bắn quay về Idle.")]
    [SerializeField] private float weaponLoweredAfter = 0.6f;
    [Tooltip("CHỈ dùng khi Animator KHÔNG có Bool IsShooting (chế độ Trigger cũ). " +
             "Chỉ kích hoạt lại animation bắn sau khoảng thời gian này. Đặt 0 để kích hoạt mỗi phát.")]
    [SerializeField] private float shootAnimationRetriggerInterval = 0.4f;
    [Tooltip("Giữ Bool IsShooting = true thêm một chút sau phát bắn cuối " +
             "(để bắn lẻ vẫn thấy trọn animation, và không bị rớt về Idle giữa 2 phát). " +
             "Nên lớn hơn khoảng cách giữa 2 phát (singleShotInterval, burstRepeatDelay).")]
    [SerializeField] private float shootHoldAfterShot = 0.35f;

    [Header("Audio - Nguồn phát")]
    [Tooltip("AudioSource dùng cho tiếng súng. Để trống thì script tự tạo.")]
    [SerializeField] private AudioSource gunAudioSource;
    [Tooltip("AudioSource dùng cho tiếng bước chân và tiếng nhảy. Để trống thì script tự tạo.")]
    [SerializeField] private AudioSource sfxAudioSource;
    [Tooltip("AudioSource dùng riêng cho tiếng nạp đạn (tách riêng để bước chân không làm lệch cao độ). " +
             "Để trống thì script tự tạo.")]
    [SerializeField] private AudioSource reloadAudioSource;

    [Header("Audio - Tiếng súng (mỗi chế độ một âm thanh)")]
    [Tooltip("Chế độ 1: AUTO (bắn liên thanh, nên chọn clip ngắn).")]
    [SerializeField] private AudioClip autoShotClip;
    [Tooltip("Chế độ 2: SINGLE (súng ngắn, từng viên).")]
    [SerializeField] private AudioClip singleShotClip;
    [Tooltip("Chế độ 3: BURST (mỗi viên trong loạt phát một lần âm thanh này).")]
    [SerializeField] private AudioClip burstShotClip;
    [Range(0f, 1f)]
    [SerializeField] private float gunVolume = 1f;
    [Tooltip("Thay đổi cao độ ngẫu nhiên mỗi phát để nghe tự nhiên hơn. 0 = tắt.")]
    [SerializeField] private float gunPitchRandom = 0.04f;

    [Header("Audio - Bước chân và nhảy")]
    [Tooltip("Kéo vài clip bước chân vào đây, script sẽ chọn ngẫu nhiên (không lặp liền nhau).")]
    [SerializeField] private AudioClip[] footstepClips;
    [Tooltip("Thời gian giữa 2 bước khi đi bộ.")]
    [SerializeField] private float walkFootstepInterval = 0.5f;
    [Tooltip("Thời gian giữa 2 bước khi chạy (giữ Shift).")]
    [SerializeField] private float runFootstepInterval = 0.3f;
    [Range(0f, 1f)]
    [SerializeField] private float footstepVolume = 0.6f;
    [SerializeField] private float footstepPitchRandom = 0.08f;
    [SerializeField] private AudioClip jumpClip;
    [Range(0f, 1f)]
    [SerializeField] private float jumpVolume = 0.8f;

    [Header("Audio - Nạp đạn")]
    [Tooltip("Tiếng nạp đạn, phát khi bấm R. Độ dài clip nên gần bằng Reload Duration.")]
    [SerializeField] private AudioClip reloadClip;
    [Range(0f, 1f)]
    [SerializeField] private float reloadVolume = 0.9f;

    [Header("Reload")]
    [SerializeField] private float reloadDuration = 1.5f;

    [Header("Bullet Tracer")]
    [SerializeField] private LineRenderer bulletTracerPrefab;
    [SerializeField] private Color bulletColor = new Color(1f, 0.84f, 0f, 1f);
    [SerializeField] private float bulletWidth = 0.035f;
    [SerializeField] private float bulletSpeed = 180f;
    [SerializeField] private float bulletTrailLength = 3f;
    [SerializeField] private float bulletLifeTime = 0.08f;

    [Header("Muzzle Flash (tia lửa đầu súng)")]
    [Tooltip("Hiệu ứng có sẵn trong Scene (đặt làm con của súng hoặc Muzzle Point). " +
             "Sẽ được Play lại mỗi phát bắn. Để trống thì script tự tìm trong Muzzle Point.")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [Tooltip("Hoặc kéo Prefab hiệu ứng vào đây: sẽ được tạo ra tại đầu súng mỗi phát bắn.")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [Tooltip("Sau bao nhiêu giây thì xoá bản Prefab đã tạo ra.")]
    [SerializeField] private float muzzleFlashPrefabLifetime = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logAnimatorParameters = true;

    // Lưu thông tin 1 parameter của Animator: có tồn tại không và kiểu gì.
    private struct AnimParam
    {
        public int hash;
        public bool exists;
        public AnimatorControllerParameterType type;
    }

    private CharacterController controller;
    private Vector3 verticalVelocity;

    // 1 = Auto, 2 = Single, 3 = Burst
    private int currentShootMode;
    private float nextShootTime;
    private float lastShotTime = -999f;
    private bool isAiming;
    private bool isReloading;
    private bool isBurstFiring;
    private bool isDead;

    // Trạng thái đưa súng lên trước khi bắn.
    private bool isRaisingWeapon;     // đang chờ súng đưa lên
    private bool raiseTriggered;      // trigger Shoot đã được kích hoạt lúc bắt đầu đưa súng
    private bool pendingFire;         // có yêu cầu bắn đang chờ súng đưa lên xong
    private float weaponReadyTime;    // thời điểm súng đưa lên xong
    private float lastBulletTime = -999f;

    private float lastShootAnimationTime = -999f;
    private float singleClickBufferUntil = -1f;   // bấm chuột được nhớ đến thời điểm này (chế độ Single)

    private float footstepTimer;
    private int lastFootstepIndex = -1;

    private int singleAmmo;              // số đạn còn lại trong băng ở chế độ Single
    private bool autoReloadPending;      // đang chờ một chút trước khi tự nạp đạn

    private AnimParam speedP, shootP, isShootingP, shootModeP, groundedP, aimingP;
    private AnimParam moveXP, moveYP, reloadP, dieP, jumpP;

    // Animator có Bool IsShooting hay không (nếu có thì dùng Bool, không dùng Trigger để bắn).
    private bool UsesShootingBool =>
        isShootingP.exists && isShootingP.type == AnimatorControllerParameterType.Bool;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        SetupAudio();
        singleAmmo = Mathf.Max(1, singleMagazineSize);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("SoldierController: Không tìm thấy Animator trong nhân vật.");
            enabled = false;
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogError("SoldierController: Animator chưa được gán Controller! " +
                           "Hãy kéo Animator Controller vào ô Controller của component Animator.");
            enabled = false;
            return;
        }

        // Nhân vật di chuyển bằng CharacterController nên không dùng Root Motion.
        animator.applyRootMotion = false;

        speedP = FindParam(speedParameter);
        shootP = FindParam(shootParameter);
        isShootingP = FindParam(isShootingParameter);
        shootModeP = FindParam(shootModeParameter);
        groundedP = FindParam(groundedParameter);
        aimingP = FindParam(aimingParameter);
        moveXP = FindParam(moveXParameter);
        moveYP = FindParam(moveYParameter);
        reloadP = FindParam(reloadParameter);
        dieP = FindParam(dieParameter);
        jumpP = FindParam(jumpParameter);

        if (logAnimatorParameters)
        {
            CheckParam(speedP, speedParameter, AnimatorControllerParameterType.Float);
            CheckParam(shootP, shootParameter, AnimatorControllerParameterType.Trigger);
            CheckParam(shootModeP, shootModeParameter, AnimatorControllerParameterType.Int);
            CheckParam(groundedP, groundedParameter, AnimatorControllerParameterType.Bool);
            CheckParam(aimingP, aimingParameter, AnimatorControllerParameterType.Bool);
            CheckParam(moveXP, moveXParameter, AnimatorControllerParameterType.Float);
            CheckParam(moveYP, moveYParameter, AnimatorControllerParameterType.Float);
            CheckParam(reloadP, reloadParameter, AnimatorControllerParameterType.Trigger);
            CheckParam(dieP, dieParameter, AnimatorControllerParameterType.Trigger);
            CheckParam(jumpP, jumpParameter, AnimatorControllerParameterType.Trigger);

            if (!isShootingP.exists)
                Debug.LogWarning("Animator chưa có Bool '" + isShootingParameter + "'. " +
                                 "Đang dùng Trigger '" + shootParameter + "' (dễ bị giật về Idle khi giữ chuột). " +
                                 "Hãy thêm Bool này vào Animator để animation bắn lặp mượt.");
            else
                CheckParam(isShootingP, isShootingParameter, AnimatorControllerParameterType.Bool);
        }

        currentShootMode = Mathf.Clamp(startingShootMode, 1, 3);
        SetIntP(shootModeP, currentShootMode);
    }

    private void Start()
    {
        GetCamera();

        if (cameraTransform == null)
            Debug.LogWarning("SoldierController: Chưa có Camera. Hãy gán Camera Transform hoặc gắn tag " +
                             "'MainCamera' cho camera. Tạm thời nhân vật sẽ di chuyển theo hướng của chính nó.");

        if (muzzlePoint == null)
            Debug.LogWarning("Chưa gán Muzzle Point. Tia đạn sẽ lấy vị trí dự phòng phía trước nhân vật.");

        // Nếu chưa gán hiệu ứng nhưng đã đặt sẵn trong Muzzle Point thì tự tìm.
        if (muzzleFlash == null && muzzleFlashPrefab == null && muzzlePoint != null)
            muzzleFlash = muzzlePoint.GetComponentInChildren<ParticleSystem>();

        if (bulletTracerPrefab == null)
            Debug.LogWarning("Chưa gán Bullet Tracer Prefab. Nhân vật vẫn bắn Raycast nhưng không hiện tia đạn.");
    }

    private void Update()
    {
        if (isDead)
            return;

        // KHÔNG return khi thiếu camera nữa, để animation/di chuyển vẫn chạy.
        HandleShootModeInput();
        HandleAimInput();
        HandleReloadInput();
        HandleJumpInput();
        HandleShootingInput();
        UpdateShootingAnimBool();
        MovePlayer();
    }

    // ---------------------------------------------------------------- Camera

    private Transform GetCamera()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        return cameraTransform;
    }

    // ---------------------------------------------------------------- Input

    private void HandleShootModeInput()
    {
        if (KeyPressedThisFrame(KeyCode.Alpha1))
            SetShootMode(1);
        else if (KeyPressedThisFrame(KeyCode.Alpha2))
            SetShootMode(2);
        else if (KeyPressedThisFrame(KeyCode.Alpha3))
            SetShootMode(3);
    }

    private void SetShootMode(int mode)
    {
        currentShootMode = Mathf.Clamp(mode, 1, 3);
        singleClickBufferUntil = -1f;
        SetIntP(shootModeP, currentShootMode);
        Debug.Log("Chế độ bắn: " + GetShootModeName());
    }

    private string GetShootModeName()
    {
        switch (currentShootMode)
        {
            case 1: return "AUTO";
            case 2: return "SINGLE";
            case 3: return "BURST";
            default: return "UNKNOWN";
        }
    }

    private void HandleAimInput()
    {
        isAiming = AimButtonHeld();
        SetBoolP(aimingP, isAiming);
    }

    private void HandleReloadInput()
    {
        if (KeyPressedThisFrame(KeyCode.R))
            StartReload();
    }

    // Bắt đầu nạp đạn (dùng cho cả bấm R và tự động nạp ở chế độ Single).
    private void StartReload()
    {
        if (isDead || isReloading || isBurstFiring)
            return;

        // Huỷ yêu cầu bắn đang chờ đưa súng lên.
        isRaisingWeapon = false;
        raiseTriggered = false;
        pendingFire = false;
        singleClickBufferUntil = -1f;

        FireTriggerP(reloadP);
        PlayReloadSound();
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        yield return new WaitForSeconds(Mathf.Max(0.05f, reloadDuration));

        // Nạp xong: đầy băng đạn chế độ Single.
        singleAmmo = Mathf.Max(1, singleMagazineSize);
        isReloading = false;
    }

    // Chế độ 2 (Single) có giới hạn đạn không.
    private bool UsesSingleAmmo()
    {
        return currentShootMode == 2 && singleNeedsReload;
    }

    // Chờ một chút cho animation và tiếng bắn chạy xong rồi tự nạp đạn.
    private IEnumerator AutoReloadAfterShot()
    {
        autoReloadPending = true;
        yield return new WaitForSeconds(Mathf.Max(0f, singleReloadDelay));
        autoReloadPending = false;

        if (singleAmmo <= 0)
        {
            Debug.Log("Single: hết đạn, tự động nạp đạn.");
            StartReload();
        }
    }

    private void HandleJumpInput()
    {
        if (KeyPressedThisFrame(KeyCode.Space) && controller.isGrounded && !isReloading)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            FireTriggerP(jumpP);
            PlaySfx(jumpClip, jumpVolume, 0f);
        }
    }

    // ---------------------------------------------------------------- Shooting

    private void HandleShootingInput()
    {
        if (isReloading || isBurstFiring)
            return;

        // Chế độ 2 hết đạn thì không bắn được, chờ tự nạp xong.
        if (UsesSingleAmmo() && singleAmmo <= 0)
            return;

        // Auto luôn dùng "giữ chuột". Single/Burst dùng "giữ chuột" nếu bật holdToFireInAllModes,
        // ngược lại chỉ bắn khi nhấn chuột.
        bool wantsFire;

        if (IsSingleClickMode())
        {
            // Súng ngắn: mỗi lần bấm = 1 viên. Nhớ cú bấm một chút để không bị mất khi đang chờ.
            if (FireButtonPressed())
                singleClickBufferUntil = Time.time + Mathf.Max(0f, singleClickBuffer);

            wantsFire = Time.time < singleClickBufferUntil;
        }
        else
        {
            wantsFire = (holdToFireInAllModes || currentShootMode == 1)
                ? FireButtonHeld()
                : FireButtonPressed();
        }

        // Đang chờ súng đưa lên thì vẫn giữ yêu cầu bắn (kể cả khi người chơi nhả chuột sớm).
        if (!wantsFire && pendingFire)
            wantsFire = true;

        if (!wantsFire)
            return;

        if (currentShootMode == 1)
        {
            // Auto: bắn liên tục theo shootCooldown.
            TryShoot();
        }
        else if (currentShootMode == 2)
        {
            // Single: mỗi phát cách nhau singleShotInterval khi giữ chuột.
            TryShoot(false, singleShotInterval);
        }
        else if (currentShootMode == 3)
        {
            // Burst: bắn một loạt, nghỉ burstRepeatDelay rồi lặp lại nếu vẫn giữ chuột.
            // Chỉ bắt đầu loạt khi súng đã đưa lên xong.
            if (Time.time >= nextShootTime && IsWeaponReadyToFire())
            {
                pendingFire = false;
                StartCoroutine(BurstRoutine());
            }
        }
    }

    // Chế độ 2 (Single) kiểu súng ngắn: phải bấm chuột cho từng viên.
    private bool IsSingleClickMode()
    {
        return currentShootMode == 2 && singleRequiresClick;
    }

    // Có đang "giữ chuột để bắn" không. Ở chế độ súng ngắn, giữ chuột KHÔNG tính là đang bắn.
    private bool IsHoldingFire()
    {
        if (IsSingleClickMode())
            return false;

        return FireButtonHeld();
    }

    // Bật/tắt Bool IsShooting của Animator.
    // Còn giữ chuột (hoặc vừa bắn xong trong shootHoldAfterShot giây) thì = true,
    // nhờ vậy state bắn được lặp liên tục và chỉ về Idle khi thật sự ngừng bắn.
    private void UpdateShootingAnimBool()
    {
        if (!UsesShootingBool)
            return;

        bool shooting = !isReloading &&
                        (IsHoldingFire() || isBurstFiring || isRaisingWeapon || pendingFire ||
                         Time.time < lastShotTime + shootHoldAfterShot);

        SetBoolP(isShootingP, shooting);
    }

    private IEnumerator BurstRoutine()
    {
        isBurstFiring = true;

        int count = Mathf.Max(1, burstBulletCount);
        for (int i = 0; i < count; i++)
        {
            if (isDead || isReloading)
                break;

            TryShoot(true);

            if (i < count - 1)
                yield return new WaitForSeconds(Mathf.Max(0.01f, burstInterval));
        }

        // Nghỉ một chút trước khi cho phép loạt tiếp theo (khi giữ chuột).
        nextShootTime = Time.time + Mathf.Max(0.01f, burstRepeatDelay);
        isBurstFiring = false;
    }

    // Trả về true khi súng đã đưa lên và được phép ra đạn.
    // Nếu súng đang hạ xuống: kích hoạt animation bắn (đưa súng lên) rồi trả về false
    // cho tới khi hết raiseWeaponDelay.
    private bool IsWeaponReadyToFire()
    {
        // Đang chờ súng đưa lên.
        if (isRaisingWeapon)
        {
            if (Time.time < weaponReadyTime)
                return false;

            isRaisingWeapon = false; // đã đưa lên xong
            return true;
        }

        bool weaponLowered = Time.time - lastBulletTime > weaponLoweredAfter;

        if (raiseWeaponDelay > 0f && weaponLowered)
        {
            // Bắt đầu đưa súng lên: chạy animation ngay, nhưng CHƯA ra đạn.
            isRaisingWeapon = true;
            raiseTriggered = true;
            pendingFire = true;
            weaponReadyTime = Time.time + raiseWeaponDelay;

            SetIntP(shootModeP, currentShootMode);

            if (UsesShootingBool)
                SetBoolP(isShootingP, true);   // Bool: vào state bắn ngay
            else
                FireTriggerP(shootP);          // Trigger: chế độ cũ

            lastShootAnimationTime = Time.time;

            return false;
        }

        return true;
    }

    private void TryShoot(bool ignoreCooldown = false, float cooldownOverride = -1f)
    {
        if (isDead || isReloading)
            return;

        if (!ignoreCooldown && Time.time < nextShootTime)
            return;

        // Burst đã kiểm tra súng sẵn sàng trước khi bắt đầu loạt nên bỏ qua ở đây.
        if (!ignoreCooldown && !IsWeaponReadyToFire())
            return;

        float cooldown = cooldownOverride > 0f ? cooldownOverride : shootCooldown;
        nextShootTime = Time.time + Mathf.Max(0.01f, cooldown);
        lastShotTime = Time.time;
        lastBulletTime = Time.time;
        pendingFire = false;
        singleClickBufferUntil = -1f;   // đã bắn, xoá cú bấm đang nhớ

        // Chế độ Single: trừ 1 viên, hết đạn thì tự nạp.
        if (UsesSingleAmmo())
        {
            singleAmmo = Mathf.Max(0, singleAmmo - 1);

            if (singleAmmo <= 0 && !autoReloadPending)
                StartCoroutine(AutoReloadAfterShot());
        }

        // Cập nhật mode trước để Any State chọn đúng clip.
        SetIntP(shootModeP, currentShootMode);

        if (UsesShootingBool)
        {
            // Dùng Bool IsShooting: animation tự lặp, KHÔNG cần trigger mỗi viên đạn.
            raiseTriggered = false;
            SetBoolP(isShootingP, true);
        }
        else if (raiseTriggered)
        {
            raiseTriggered = false;
        }
        else
        {
            // Chế độ Trigger cũ (khi Animator chưa có Bool IsShooting).
            float animationInterval = Mathf.Max(0f, shootAnimationRetriggerInterval);

            if (animationInterval <= 0f ||
                Time.time >= lastShootAnimationTime + animationInterval)
            {
                FireTriggerP(shootP);
                lastShootAnimationTime = Time.time;
            }
        }

        Vector3 muzzlePosition = muzzlePoint != null
            ? muzzlePoint.position
            : transform.position + transform.forward * 0.5f + Vector3.up;

        // Đạn xuất phát từ đầu nòng súng (Muzzle Point).
        Vector3 origin = muzzlePosition;
        Vector3 direction;

        if (aimAtCrosshair)
        {
            // Bắn từ nòng súng nhưng bay về điểm giữa màn hình (tâm ngắm).
            direction = GetCrosshairDirection(origin);
        }
        else
        {
            // Bay thẳng theo hướng nòng súng (trục Z của Muzzle Point).
            direction = muzzlePoint != null ? muzzlePoint.forward : transform.forward;
        }

        Vector3 targetPoint = origin + direction * shootRange;

        if (RaycastIgnoreSelf(origin, direction, out RaycastHit hit))
        {
            targetPoint = hit.point;
            Debug.DrawLine(origin, hit.point, Color.yellow, 1f);
            Debug.Log("Đã bắn trúng: " + hit.collider.name);
        }
        else
        {
            Debug.DrawLine(origin, targetPoint, Color.red, 1f);
        }

        PlayMuzzleFlash(origin, direction);
        PlayGunSound();

        if (bulletTracerPrefab != null)
            StartCoroutine(SpawnBulletTracer(muzzlePosition, targetPoint));
    }

    // Tính hướng bắn từ nòng súng (origin) tới điểm mà tâm ngắm (giữa màn hình) đang chỉ vào.
    private Vector3 GetCrosshairDirection(Vector3 origin)
    {
        Vector3 fallback = muzzlePoint != null ? muzzlePoint.forward : transform.forward;
        Transform cam = GetCamera();

        if (cam == null)
            return fallback;

        // Tia từ tâm màn hình. Bắt đầu tia ở mặt phẳng ngang với nòng súng (không phải ngay tại camera)
        // để không trúng vật nằm giữa camera và nhân vật.
        float along = Mathf.Max(0f, Vector3.Dot(origin - cam.position, cam.forward));
        Vector3 rayStart = cam.position + cam.forward * along;

        Vector3 aimPoint = rayStart + cam.forward * shootRange;

        if (RaycastIgnoreSelf(rayStart, cam.forward, out RaycastHit camHit))
            aimPoint = camHit.point;

        Vector3 toAim = aimPoint - origin;

        // Điểm ngắm quá sát hoặc nằm sau nòng súng thì bắn thẳng theo hướng camera.
        if (toAim.sqrMagnitude < 0.01f || Vector3.Dot(toAim, cam.forward) <= 0f)
            return cam.forward;

        return toAim.normalized;
    }

    // Phát hiệu ứng tia lửa tại đầu súng.
    private void PlayMuzzleFlash(Vector3 position, Vector3 direction)
    {
        // Hiệu ứng đặt sẵn: dừng rồi chạy lại để mỗi phát đều thấy lửa (kể cả khi bắn liên tục).
        if (muzzleFlash != null)
        {
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlash.Play(true);
        }

        // Prefab: tạo mới tại đầu súng, gắn vào Muzzle Point để đi theo súng.
        if (muzzleFlashPrefab != null)
        {
            Quaternion rotation = direction.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(direction)
                : transform.rotation;

            GameObject fx = Instantiate(muzzleFlashPrefab, position, rotation, muzzlePoint);
            Destroy(fx, Mathf.Max(0.05f, muzzleFlashPrefabLifetime));
        }
    }

    // Raycast lấy vật đầu tiên trúng đạn, BỎ QUA collider của chính nhân vật.
    private bool RaycastIgnoreSelf(Vector3 origin, Vector3 direction, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, shootRange, hitLayers,
                                               QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(transform))
                continue;

            result = h;
            return true;
        }

        result = default;
        return false;
    }

    // ---------------------------------------------------------------- Movement

    // Đang bắn = giữ chuột trái, đang bắn loạt Burst, hoặc vừa bắn xong trong thời gian ngắn.
    // Khi đang nạp đạn thì không tính là đang bắn (vẫn di chuyển được).
    private bool IsShootingNow()
    {
        if (!stopMovingWhileShooting || isReloading)
            return false;

        return IsHoldingFire() || isBurstFiring || isRaisingWeapon ||
               Time.time < lastShotTime + standStillAfterShot;
    }

    private void MovePlayer()
    {
        float horizontal = GetHorizontalInput();
        float vertical = GetVerticalInput();

        Vector2 input2D = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);

        // Nếu chưa có camera thì dùng chính nhân vật làm hệ quy chiếu.
        Transform reference = GetCamera() != null ? cameraTransform : transform;

        Vector3 forward = reference.forward;
        Vector3 right = reference.right;
        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude > 0.001f) forward.Normalize();
        if (right.sqrMagnitude > 0.001f) right.Normalize();

        Vector3 moveDirection = forward * input2D.y + right * input2D.x;
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        // Xoay thân nhân vật theo hướng camera (kiểu TPS).
        if (reference != transform && forward.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(forward);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        bool isRunning = RunPressed();
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        Vector3 movement = moveDirection * currentSpeed;

        // Đang bắn thì đứng yên (vẫn xoay theo camera để ngắm, trọng lực vẫn hoạt động).
        // Đặt input2D về 0 để Animator chuyển về Idle/đứng bắn thay vì chạy tại chỗ.
        if (IsShootingNow())
        {
            movement = Vector3.zero;
            input2D = Vector2.zero;
        }

        if (controller.isGrounded && verticalVelocity.y < 0f)
            verticalVelocity.y = -2f;

        verticalVelocity.y += gravity * Time.deltaTime;
        movement.y = verticalVelocity.y;

        controller.Move(movement * Time.deltaTime);

        // Speed dùng cho Idle <-> Run.
        SetFloatP(speedP, input2D.magnitude);
        SetBoolP(groundedP, controller.isGrounded);

        // input2D đã về 0 khi đang đứng bắn nên sẽ không phát tiếng bước chân lúc đó.
        HandleFootsteps(input2D.sqrMagnitude > 0.01f, isRunning);

        // Chọn hướng chính để điều khiển 4 state Walk*_Shoot_AR (chỉ khi đang ngắm).
        float moveX = 0f;
        float moveY = 0f;

        if (isAiming && input2D.sqrMagnitude > 0.01f)
        {
            if (Mathf.Abs(input2D.x) > Mathf.Abs(input2D.y))
                moveX = Mathf.Sign(input2D.x);
            else
                moveY = Mathf.Sign(input2D.y);
        }

        SetFloatP(moveXP, moveX);
        SetFloatP(moveYP, moveY);
    }

    private IEnumerator SpawnBulletTracer(Vector3 startPosition, Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - startPosition).normalized;
        float distance = Vector3.Distance(startPosition, targetPosition);

        if (distance <= 0.01f)
            yield break;

        LineRenderer tracer = Instantiate(bulletTracerPrefab, startPosition, Quaternion.identity);
        tracer.useWorldSpace = true;
        tracer.positionCount = 2;
        tracer.startWidth = bulletWidth;
        tracer.endWidth = bulletWidth * 0.5f;
        tracer.startColor = bulletColor;
        tracer.endColor = bulletColor;

        float travelTime = distance / Mathf.Max(1f, bulletSpeed);
        float elapsed = 0f;

        while (elapsed < travelTime)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / travelTime);

            Vector3 bulletHead = Vector3.Lerp(startPosition, targetPosition, progress);
            Vector3 bulletTail = bulletHead - direction * bulletTrailLength;

            if (progress < bulletTrailLength / distance)
                bulletTail = startPosition;

            tracer.SetPosition(0, bulletTail);
            tracer.SetPosition(1, bulletHead);
            yield return null;
        }

        Destroy(tracer.gameObject, bulletLifeTime);
    }

    // Gọi hàm này từ hệ thống máu khi HP về 0.
    public void PlayDeathAnimation()
    {
        if (isDead)
            return;

        isDead = true;
        StopAllCoroutines();

        FireTriggerP(dieP);

        // Đặt thẳng về 0 (không damp) vì Update đã dừng chạy sau khi chết.
        SetFloatP(speedP, 0f, false);
        SetFloatP(moveXP, 0f, false);
        SetFloatP(moveYP, 0f, false);
        SetBoolP(aimingP, false);
        SetBoolP(isShootingP, false);

        if (reloadAudioSource != null)
            reloadAudioSource.Stop();
    }

    // ---------------------------------------------------------------- Audio

    private void SetupAudio()
    {
        if (gunAudioSource == null)
            gunAudioSource = CreateAudioSource();

        if (sfxAudioSource == null)
            sfxAudioSource = CreateAudioSource();

        if (reloadAudioSource == null)
            reloadAudioSource = CreateAudioSource();
    }

    private AudioSource CreateAudioSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;   // 2D: nghe rõ như nhau dù camera đứng ở đâu
        return source;
    }

    // Phát tiếng súng theo chế độ bắn hiện tại (1 = Auto, 2 = Single, 3 = Burst).
    private void PlayGunSound()
    {
        AudioClip clip;

        switch (currentShootMode)
        {
            case 1: clip = autoShotClip; break;
            case 2: clip = singleShotClip; break;
            default: clip = burstShotClip; break;
        }

        if (clip == null || gunAudioSource == null)
            return;

        gunAudioSource.pitch = 1f + Random.Range(-gunPitchRandom, gunPitchRandom);
        gunAudioSource.PlayOneShot(clip, gunVolume);
    }

    // Phát tiếng nạp đạn (bắt đầu cùng lúc với animation nạp).
    private void PlayReloadSound()
    {
        if (reloadClip == null || reloadAudioSource == null)
            return;

        reloadAudioSource.Stop();
        reloadAudioSource.pitch = 1f;
        reloadAudioSource.PlayOneShot(reloadClip, reloadVolume);
    }

    // Phát một âm thanh hiệu ứng (nhảy, bước chân...) trên sfxAudioSource.
    private void PlaySfx(AudioClip clip, float volume, float pitchRandom)
    {
        if (clip == null || sfxAudioSource == null)
            return;

        sfxAudioSource.pitch = 1f + Random.Range(-pitchRandom, pitchRandom);
        sfxAudioSource.PlayOneShot(clip, volume);
    }

    // Phát tiếng bước chân đều đặn khi đang đứng trên đất và di chuyển.
    // Chạy (Shift) thì bước nhanh hơn đi bộ.
    private void HandleFootsteps(bool isMoving, bool running)
    {
        if (!isMoving || !controller.isGrounded)
        {
            footstepTimer = 0f;   // bước đầu tiên sẽ phát ngay khi bắt đầu đi
            return;
        }

        footstepTimer -= Time.deltaTime;

        if (footstepTimer > 0f)
            return;

        footstepTimer = Mathf.Max(0.05f, running ? runFootstepInterval : walkFootstepInterval);

        if (footstepClips == null || footstepClips.Length == 0)
            return;

        // Chọn clip ngẫu nhiên nhưng không trùng clip vừa phát.
        int index = Random.Range(0, footstepClips.Length);

        if (footstepClips.Length > 1 && index == lastFootstepIndex)
            index = (index + 1) % footstepClips.Length;

        lastFootstepIndex = index;
        PlaySfx(footstepClips[index], footstepVolume, footstepPitchRandom);
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
            Debug.LogWarning("Animator đang THIẾU parameter: '" + parameterName + "' (cần kiểu " + expected + ")");
        else if (p.type != expected)
            Debug.LogWarning("Parameter '" + parameterName + "' đang là kiểu " + p.type +
                             " nhưng nên là " + expected + ".");
    }

    // Float: chấp nhận Float / Int / Bool.
    private void SetFloatP(AnimParam p, float value, bool damp = true)
    {
        if (!p.exists) return;

        switch (p.type)
        {
            case AnimatorControllerParameterType.Float:
                if (damp) animator.SetFloat(p.hash, value, 0.08f, Time.deltaTime);
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

    // Int: chấp nhận Int / Float.
    private void SetIntP(AnimParam p, int value)
    {
        if (!p.exists) return;

        switch (p.type)
        {
            case AnimatorControllerParameterType.Int:
                animator.SetInteger(p.hash, value);
                break;
            case AnimatorControllerParameterType.Float:
                animator.SetFloat(p.hash, value);
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

    // ---------------------------------------------------------------- Input wrappers

    private float GetHorizontalInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return 0f;
        float value = 0f;
        if (Keyboard.current.aKey.isPressed) value -= 1f;
        if (Keyboard.current.dKey.isPressed) value += 1f;
        return value;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxisRaw("Horizontal");
#else
        return 0f;
#endif
    }

    private float GetVerticalInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return 0f;
        float value = 0f;
        if (Keyboard.current.sKey.isPressed) value -= 1f;
        if (Keyboard.current.wKey.isPressed) value += 1f;
        return value;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxisRaw("Vertical");
#else
        return 0f;
#endif
    }

    private bool RunPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift);
#else
        return false;
#endif
    }

    private bool KeyPressedThisFrame(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;

        switch (key)
        {
            case KeyCode.Alpha1:
                return Keyboard.current.digit1Key.wasPressedThisFrame ||
                       Keyboard.current.numpad1Key.wasPressedThisFrame;
            case KeyCode.Alpha2:
                return Keyboard.current.digit2Key.wasPressedThisFrame ||
                       Keyboard.current.numpad2Key.wasPressedThisFrame;
            case KeyCode.Alpha3:
                return Keyboard.current.digit3Key.wasPressedThisFrame ||
                       Keyboard.current.numpad3Key.wasPressedThisFrame;
            case KeyCode.R:
                return Keyboard.current.rKey.wasPressedThisFrame;
            case KeyCode.Space:
                return Keyboard.current.spaceKey.wasPressedThisFrame;
            default:
                return false;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(key);
#else
        return false;
#endif
    }

    private bool AimButtonHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    private bool FireButtonHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    private bool FireButtonPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }
}