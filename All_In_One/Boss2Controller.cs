using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using KartGame.KartSystems;
using UnityEngine.SceneManagement;

public enum BossPhase { Phase1 = 0, Phase2 = 1, Phase2_5 = 2, Phase3 = 3 }
public enum BossState { Idle, UsingSkill, Stunned, Defeated }

[System.Serializable]
public struct BossSkillRanges
{
    [Tooltip("스킬 전반적인 영향 범위")]
    public float skillRadius;

    [Tooltip("1단계: 근거리 근접 공격 범위 (내곽)")]
    public float innerRadius;

    [Tooltip("2단계: 중간 범위 (중곽)")]
    public float midRadius;

    [Tooltip("3단계: 최대 외곽 범위 (외곽)")]
    public float outerRadius;
}

[System.Serializable]
public struct PhaseSpawnData
{
    public string phaseName;

    [Header("Grid Spawn Area Settings")]
    [Tooltip("스폰 영역의 중심점 (비워둘 경우 보스 위치 기준)")]
    public Transform spawnAreaCenter;

    [Tooltip("스폰 사각형 영역의 크기 (X: 가로, Y: 세로)")]
    public Vector2 spawnAreaSize;

    [Tooltip("그리드 한 칸의 크기 (최소 1 이상 권장)")]
    public float cellSize;

    [Header("Spawn Counts")]
    [Tooltip("생성할 가벼운 프랍 개수")]
    public int lightPropCount;

    [Tooltip("생성할 무거운 프랍 개수")]
    public int heavyPropCount;

    [Tooltip("생성할 스프링클러 개수")]
    public int sprinklerCount;
}

[System.Serializable]
public struct PhaseDetachedPropData
{
    [Tooltip("비활성화할 보스 부위 메쉬/콜라이더 오브젝트들")]
    public GameObject[] partsToDisable;

    [Tooltip("파괴 위치에 생성할 무게감 있는 프랍들")]
    public GameObject[] heavyPropPrefabsToDrop;
}

public class Boss2Controller : MonoBehaviour
{
    [Header("1. Core References & Status")]
    [Tooltip("보스가 활성화 상태일 때 켜지고, 기절/사망 시 자동으로 꺼지는 이펙트 오브젝트")]
    public GameObject bossEffect;
    [Tooltip("Rig 및 IK 회전에 직접 사용되는 모델의 최상위 트랜스폼")]
    public Transform visualRoot;
    [Tooltip("플레이어")]
    public GameObject playerObj;
    [Tooltip("플레이어의 카트")]
    public GameObject bumperObj;
    [Tooltip("스킬별 이펙트/사운드 출력 아웃풋 노드")]
    public _Project.Scripts.VisualScripting.Boss2SkillEffectOutput skillEffectOutput;
    [Tooltip("스킬별 카메라 셰이크 제어 스크립트")]
    public _Project.Scripts.VisualScripting.Boss2CameraShaker cameraShaker;
    [Tooltip("지면 레이어 (기본값: Default)")]
    public LayerMask groundLayer = 1 << 0;
    public LayerMask targetLayer;
    public LayerMask chargeTargetLayer;

    [Space(5)]
    public BossPhase currentPhase = BossPhase.Phase1;
    public BossState currentState = BossState.Idle;


    [Header("2. Battery System Settings")]
    [Tooltip("현재 배터리 양 (0 ~ 100)")]
    public float currentBattery = 0f;
    [Tooltip("최대 배터리 양")]
    public float maxBattery = 100f;
    [Tooltip("충전 상태일 때 초당 배터리 증가량")]
    public float batteryChargeRate = 0.5f;
    [Tooltip("2페이즈일 때 초당 배터리 감소량")]
    public float batteryDrainRate = 1.0f;

    private bool isCharging = false;


    [Header("3. AI & Manual Controls")]
    [Tooltip("체크 해제 시 보스 AI 동작이 중지되고 수동 디버그 키 입력 모드가 됩니다.")]
    public bool enableAI = true;
    [Tooltip("AI ON/OFF 토글 키 (디버그 모드 전환)")]
    public KeyCode toggleAIKey = KeyCode.Alpha0;
    [Tooltip("스킬 후 다음 행동을 판단하기까지의 대기 시간")]
    public float patternDecisionInterval = 2.0f;

    [Space(5)]
    [Tooltip("Manual Skill Debug Keys")]
    public KeyCode chargeKey = KeyCode.C;
    public KeyCode relocateKey = KeyCode.R;
    public KeyCode throwPropKey = KeyCode.T;
    public KeyCode throwHeavyPropKey = KeyCode.Y;
    public KeyCode bunshinKey = KeyCode.I;
    public KeyCode spawnPropsKey = KeyCode.O;
    public KeyCode fanSpinKey = KeyCode.F;
    public KeyCode garenEKey = KeyCode.E; // [추가] GarenE 수동 디버그 키
    public KeyCode woofer1Key = KeyCode.G;
    public KeyCode wooferRushKey = KeyCode.H;
    public KeyCode koongKey = KeyCode.K;
    public KeyCode pullPushKey = KeyCode.P;
    public KeyCode punchKey = KeyCode.Alpha1;
    public KeyCode flailKey = KeyCode.Alpha2;
    public KeyCode sixPunchKey = KeyCode.Alpha3;


    [Header("4. General Physics, Movement & Ranges")]
    [Tooltip("스킬 범위 측정의 중심점 피벗 (비워둘 경우 보스 Transform 위치 사용)")]
    public Transform skillPivot;

    public Vector3 SkillPivotPosition => skillPivot != null ? skillPivot.position : transform.position;

    public BossSkillRanges skillRanges = new BossSkillRanges
    {
        skillRadius = 600f,
        innerRadius = 180f,
        midRadius = 300f,
        outerRadius = 420f
    };
    public float lowerAmount = 20.0f;
    public float moveSpeed = 20.0f;

    [Space(5)]
    [Tooltip("Relocate Skill Settings")]
    public Transform moveCenterTarget;
    public float moveAroundRadius = 15.0f;
    public float relocationSpeed = 40.0f;
    public float moveObstacleCheckRadius = 10.0f;

    [Space(5)]
    [Tooltip("SphereCast Collision Settings")]
    public Vector3 sphereCastOffset = new Vector3(0.0f, 80.0f, 5.0f);
    public float castRadius = 40.0f;
    public float bossEjectForce = 60f;


    [Header("5. Global & Skill Cooldowns")]
    public float globalCooldown = 3.0f;
    public float subSkillCooldownDuration = 12.0f;
    public float fanSpinCooldown = 5.0f;
    public float garenECooldown = 5.0f; // [추가] GarenE 쿨타임
    public float woofer1Cooldown = 8.0f;
    public float chargeCooldown = 10.0f;
    public float wooferRushCooldown = 10.0f;
    public float koongCooldown = 4.0f;
    public float punchCooldown = 6.0f;
    public float flailCooldown = 8.0f;
    public float phase3SubSkillCooldown = 10.0f;
    public float sixPunchCooldown = 20.0f;

    private float gcdTimer = 0f;
    private float fanSpinTimer = 0f;
    private float garenETimer = 0f; // [추가] GarenE 타이머
    private float spawnPropsTimer = 0f;
    private float bunshinTimer = 0f;
    private float throwPropTimer = 0f;
    private float woofer1Timer = 0f;
    private float chargeTimer = 0f;
    private float pullPushTimer = 0f;
    private float punchTimer = 0f;
    private float flailTimer = 0f;
    private float wooferRushTimer = 0f;
    private float koongTimer = 0f;
    private float phase3SubSkillTimer = 0f;
    private float sixPunchTimer = 0f;


    [Header("6. Phase Transition, Stun & Recovery")]
    public Transform phase2StartPoint;
    public float phase2TransitionStunDelay = 5.0f;

    [Space(5)]
    public Transform phase2_5ChargePoint;
    public float phase2_5MoveSpeed = 120.0f;

    [Space(5)]
    public Transform phase1JumpTarget;
    public Transform phase3JumpTarget;
    public float resetJumpHeight = 5.0f;
    public float resetJumpDuration = 1.2f;

    [Space(5)]
    public float stunDuration = 15.0f;
    public float recoveryDelay = 1.0f;
    public Transform kartResetPoint;
    public Transform playerResetPoint;
    public float resetPushDuration = 0.8f;
    public float playerWalkToKartSpeed = 5.0f;

    [Space(5)]
    public PhaseDetachedPropData phase1DetachData;
    public PhaseDetachedPropData phase2DetachData;
    public float detachDropForce = 15f;

    [Space(5)]
    public int waterStack = 0;
    public float waterZoneCheckRadius = 5.0f;
    private HashSet<Collider> currentWaterZones = new HashSet<Collider>();
    private bool isWaterStunned = false;


    [Header("7. Skill: Two Punch")]
    public Rig punchRig;
    public Transform punchRigTarget;
    public float punchStepDistance = 2.0f;
    public float punchDashSpeed = 10.0f;
    public float punchRotationSpeed = 12.0f;
    public float punchRigWeightSpeed = 8.0f;
    public float punchKnockbackForce = 80.0f;
    public float punchPrepTime = 0.5f;
    public float punchStateFallbackDuration = 1.0f;

    [Header("Separate Hitbox References")]
    public BossHitbox[] punchHitboxes;


    [Header("8. Skill: Flail Swing")]
    public Transform flailTransform;
    public List<Collider> flailColliders = new List<Collider>();
    public float flailSwingSpeed = 5.0f;
    public float flailSwingAngle = 90.0f;
    public float flailBlastForce = 110f;
    public BossHitbox[] flailHitboxes;


    [Header("9. Skill: Koong (Slam Attack)")]
    public float koongJumpHeight = 35.0f;
    public float koongMoveSpeed = 80.0f;
    public float koongFollowDuration = 2.5f;
    public float koongAirHoverTime = 0.8f;
    public float koongOvershootDepth = 2.0f;
    public float koongReturnSpeed = 15.0f;
    public float koongImpactRadius = 30.0f;
    public float koongKnockbackForce = 120.0f;


    [Header("10. Skill: Charge Attack & MTE")]
    public Vector3 chargeHitOffset = new Vector3(0f, 30f, 0f);
    public float chargePrepTime = 1.5f;
    public float chargeDistance = 240.0f;
    public float chargeSpeed = 320.0f;
    public float chargeHitRadius = 25.0f;
    public float chargeHitForce = 160.0f;
    public _Project.Scripts.VisualScripting.MTEProcess chargeMTEProcess;


    [Header("11. Skill: Woofer & Woofer Rush")]
    public float woofer1ApproachDistance = 15.0f;
    public float woofer1DisableInputDuration = 1.5f;
    public float innerKnockbackForce = 75f;
    public float midKnockbackForce = 60f;
    public float outerKnockbackForce = 50f;

    [Space(5)]
    public float wooferRushSpeed = 220.0f;
    public float wooferRushHitForce = 150.0f;
    public float phase3WooferRushSpeedMultiplier = 1.5f;


    [Header("12. Skill: Pull & Push / Fan Spin")]
    public float holdDuration = 8.0f;
    public float fanDuration = 3.0f;
    public float fanPushForce = 90f;


    [Header("13. Skill: Bunshin & Straight Shot")]
    public GameObject bunshinPrefab;
    public Transform bunshinSpawnPoint;
    public float fireInterval = 0.5f;
    public float bunshinShootSpeed = 150f;
    public float spreadAngle = 15f;

    [Space(5)]
    public List<GameObject> straightShotPrefabs = new List<GameObject>();
    public Transform straightShotSpawnPoint;
    public float straightShotSpeed = 150f;
    public float straightShotExtraDelay = 0.3f;

    [Space(5)]
    public int totalBunshinCount = 9;
    public int bunshinIntervalForStraightShot = 3;


    [Header("14. Prop & Field Spawn System")]
    public PhaseSpawnData[] phaseSpawnDataArray = new PhaseSpawnData[3];
    public Transform spawnSkillFacingTarget;
    public float spawnAnimDelay = 1.2f;

    [Space(5)]
    public List<GameObject> lightPropPrefabs = new List<GameObject>();
    public List<GameObject> heavyPropPrefabs = new List<GameObject>();
    public float spawnHeightOffset = 100f;
    public float lightPushSpeed = 150f;
    public float heavyPushSpeed = 120f;
    public float propBlastForce = 120f;

    [Space(5)]
    public float throwPropSpeed = 130f;
    public float heavyThrowPropSpeed = 160f;
    public Vector3 propSpawnOffset = new Vector3(0f, 10f, 0f);
    public Transform lightPropHoldPoint;
    public Transform heavyPropHoldPoint;
    public float pullAnimDuration = 1.0f;
    public float throwReleaseDelay = 0.6f;

    [Space(5)]
    public GameObject sprinklerPrefab;
    private List<GameObject> activeSprinklers = new List<GameObject>();

    [Header("Scene Transition Settings")]
    public string nextSceneName = "";

    [Header("Fullscreen Pass Effect")]
    [SerializeField] private WooferFullscreenEffect wooferFullscreenEffect;

    [Header("Charge State Outputs")]
    [SerializeField] private List<_Project.Scripts.VisualScripting.ProcessBase> chargePrepDirectOutputs;
    [SerializeField] private List<_Project.Scripts.VisualScripting.ProcessBase> chargeFailDirectOutputs;
    private bool _hasPlayerHitDuringCharge = false;

    [Header("WooferRush Hit Outputs")]
    [SerializeField] private List<_Project.Scripts.VisualScripting.ProcessBase> wooferRushHitOutputs;

    [Header("15. Debug System Settings")]
    [Tooltip("체크 시 AI가 완전히 중지되며 수동 스킬 테스트 모드가 됩니다.")]
    public bool isDebug = false;
    [Tooltip("디버그 모드 ON/OFF 토글 키")]
    public KeyCode toggleDebugKey = KeyCode.F12;

    [Header("Debug Phase Toggle Keys")]
    public KeyCode phase1Key = KeyCode.F1;
    public KeyCode phase2Key = KeyCode.F2;
    public KeyCode phase2_5Key = KeyCode.F3;
    public KeyCode phase3Key = KeyCode.F4;

    // --- Internal State & Reference Caches ---
    private Animator anim;
    private Boss2RigController rigController;

    private Coroutine aiCoroutine;
    private Coroutine recoveryCoroutine;
    private Coroutine movePositionCoroutine;

    private bool isVulnerable = false;
    private bool phaseSpawnExecuted = false;
    private Vector3 originalPosition;
    private HashSet<string> usedSkillsInCurrentPhase = new HashSet<string>();

    public System.Action<float, float> OnBatteryChanged;

    // --- Animator Hash Parameters ---
    private readonly int hashPhase = Animator.StringToHash("Phase");
    private readonly int hashCharge = Animator.StringToHash("Charge");
    private readonly int hashDuringCharge = Animator.StringToHash("DuringCharge");
    private readonly int hashChargeAndBoom = Animator.StringToHash("ChargeAndBoom");
    private readonly int hashChargeAndCrash = Animator.StringToHash("ChargeAndCrash");
    private readonly int hashIsDead = Animator.StringToHash("IsDead");
    private readonly int hashDropProp = Animator.StringToHash("DropProp");
    private readonly int hashDropPropByLand = Animator.StringToHash("DropPropByLand");
    private readonly int hashDropPropByElec = Animator.StringToHash("DropPropByElec");
    private readonly int hashFanSpin = Animator.StringToHash("FanSpin");
    private readonly int hashGarenE = Animator.StringToHash("GarenE");
    private readonly int hashKoong = Animator.StringToHash("Koong");
    private readonly int hashMinigun = Animator.StringToHash("Minigun");
    private readonly int hashMovePosition = Animator.StringToHash("MovePosition");
    private readonly int hashIsStunned = Animator.StringToHash("IsStunned");
    private readonly int hashThrowProp = Animator.StringToHash("ThrowProp");
    private readonly int hashWooperAttack = Animator.StringToHash("WooperAttack");
    private readonly int hashWooperRush = Animator.StringToHash("WooperRush");
    private readonly int hashKoongAfter = Animator.StringToHash("KoongAfter");
    private readonly int hashPunch1 = Animator.StringToHash("Punch1");
    private readonly int hashPunch2 = Animator.StringToHash("Punch2");
    private readonly int hashFlailSwing = Animator.StringToHash("FlailSwing");
    private readonly int hashPunch3 = Animator.StringToHash("Punch3");
    private readonly int hashDebug = Animator.StringToHash("Debug"); // [추가] Debug 해시 파라미터
    private readonly int hashUsingSkill = Animator.StringToHash("UsingSkill");

    private LayerMask CombinedTargetLayer => (targetLayer.value != 0 || chargeTargetLayer.value != 0)
        ? (targetLayer | chargeTargetLayer)
        : ~0;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        if (anim == null)
        {
            Debug.LogWarning("[Boss2Controller] Animator를 찾을 수 없습니다.");
        }

        rigController = GetComponent<Boss2RigController>();
        if (rigController == null)
        {
            rigController = GetComponentInParent<Boss2RigController>();
        }

        if (skillEffectOutput == null)
        {
            skillEffectOutput = GetComponent<_Project.Scripts.VisualScripting.Boss2SkillEffectOutput>();
        }

        if (cameraShaker == null)
        {
            cameraShaker = GetComponent<_Project.Scripts.VisualScripting.Boss2CameraShaker>();
        }

        UpdateAnimator();
    }

    private void Start()
    {
        if (currentPhase == BossPhase.Phase1)
        {
            currentBattery = 0f;
        }

        originalPosition = transform.position;
        UpdateAnimator();

        aiCoroutine = StartCoroutine(AILoopRoutine());
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleDebugKey))
        {
            isDebug = !isDebug;
            UpdateAnimator(); // [추가] 디버그 모드 토글 시 애니메이터 즉시 갱신
            Debug.Log($"<color=cyan>[Boss2Controller] Debug Mode Switch: {isDebug}</color>");
        }

        if (Input.GetKeyDown(toggleAIKey))
        {
            enableAI = !enableAI;
            Debug.Log($"[Boss2Controller] Boss AI Enabled: {enableAI}");
        }

        if (isDebug)
        {
            if (Input.GetKeyDown(phase1Key)) SetDebugPhase(BossPhase.Phase1);
            else if (Input.GetKeyDown(phase2Key)) SetDebugPhase(BossPhase.Phase2);
            else if (Input.GetKeyDown(phase2_5Key)) SetDebugPhase(BossPhase.Phase2_5);
            else if (Input.GetKeyDown(phase3Key)) SetDebugPhase(BossPhase.Phase3);
        }

        if (currentState == BossState.Idle)
        {
            bool canExecuteInput = isDebug || (!enableAI) || (Input.anyKeyDown && gcdTimer <= 0f);

            if (canExecuteInput)
            {
                HandleDebugAndManualInputs();
            }
        }

        UpdateTimers();
        UpdateBattery();

        CheckWaterZoneInPhase3();
    }

    private void SetDebugPhase(BossPhase phase)
    {
        currentPhase = phase;
        UpdateAnimator();
        Debug.Log($"<color=yellow>[Boss2Controller] [Debug] Phase Forced Change: {currentPhase}</color>");
    }

    private void HandleDebugAndManualInputs()
    {
        if (Input.GetKeyDown(chargeKey)) TriggerDebugSkill(ExecuteChargeAttackSkill());
        else if (Input.GetKeyDown(relocateKey)) TriggerDebugSkill(ExecuteRelocateSkillWrapper());
        else if (Input.GetKeyDown(pullPushKey)) TriggerDebugSkill(ExecutePullAndPushSkill());
        else if (Input.GetKeyDown(throwPropKey)) TriggerDebugSkill(ExecuteThrowPropSkill());
        else if (Input.GetKeyDown(throwHeavyPropKey)) TriggerDebugSkill(ExecuteThrowHeavyPropSkill());
        else if (Input.GetKeyDown(bunshinKey)) TriggerDebugSkill(ExecuteBunshinAndStraightShotSkill());
        else if (Input.GetKeyDown(spawnPropsKey)) TriggerDebugSkill(ExecuteSpawnPropsSkill());
        else if (Input.GetKeyDown(fanSpinKey)) TriggerDebugSkill(ExecuteFanSpinSkill());
        else if (Input.GetKeyDown(garenEKey)) TriggerDebugSkill(ExecuteGarenESkill()); // [추가] GarenE 키 매핑
        else if (Input.GetKeyDown(woofer1Key)) TriggerDebugSkill(ExecuteWoofer1Skill());
        else if (Input.GetKeyDown(wooferRushKey)) TriggerDebugSkill(ExecuteWooferRush());
        else if (Input.GetKeyDown(koongKey)) TriggerDebugSkill(ExecuteKoongSkill());
        else if (Input.GetKeyDown(punchKey)) TriggerDebugSkill(ExecuteTwoPunchSkill());
        else if (Input.GetKeyDown(flailKey)) TriggerDebugSkill(ExecuteFlailSwingSkill());
        else if (Input.GetKeyDown(sixPunchKey)) TriggerDebugSkill(ExecuteSixPunchSkill());

    }

    private void TriggerDebugSkill(IEnumerator skillRoutine)
    {
        ResetSkillAnimatorTriggers();
        StartCoroutine(skillRoutine);
    }

    private void ResetSkillAnimatorTriggers()
    {
        if (anim == null) return;

        anim.ResetTrigger("ChargeTrigger");
        anim.ResetTrigger(hashChargeAndBoom);
        anim.ResetTrigger(hashChargeAndCrash);
        anim.ResetTrigger(hashDropProp);
        anim.ResetTrigger(hashDropPropByLand);
        anim.ResetTrigger(hashDropPropByElec);
        anim.ResetTrigger(hashFanSpin);
        anim.ResetTrigger(hashGarenE); // [추가] GarenE 트리거 리셋
        anim.ResetTrigger(hashKoong);
        anim.ResetTrigger(hashKoongAfter);
        anim.ResetTrigger(hashThrowProp);
        anim.ResetTrigger(hashWooperAttack);
        anim.ResetTrigger(hashWooperRush);
        anim.ResetTrigger(hashPunch1);
        anim.ResetTrigger(hashPunch2);
        anim.ResetTrigger(hashFlailSwing);
    }

    private void UpdateTimers()
    {
        if (gcdTimer > 0f) gcdTimer -= Time.deltaTime;
        if (fanSpinTimer > 0f) fanSpinTimer -= Time.deltaTime;
        if (garenETimer > 0f) garenETimer -= Time.deltaTime; // [추가] GarenE 타이머 갱신
        if (spawnPropsTimer > 0f) spawnPropsTimer -= Time.deltaTime;
        if (bunshinTimer > 0f) bunshinTimer -= Time.deltaTime;
        if (throwPropTimer > 0f) throwPropTimer -= Time.deltaTime;
        if (woofer1Timer > 0f) woofer1Timer -= Time.deltaTime;
        if (chargeTimer > 0f) chargeTimer -= Time.deltaTime;
        if (pullPushTimer > 0f) pullPushTimer -= Time.deltaTime;
        if (koongTimer > 0f) koongTimer -= Time.deltaTime;
        if (wooferRushTimer > 0f) wooferRushTimer -= Time.deltaTime;
        if (phase3SubSkillTimer > 0f) phase3SubSkillTimer -= Time.deltaTime;
        if (sixPunchTimer > 0f) sixPunchTimer -= Time.deltaTime;

        if (punchTimer > 0f) punchTimer -= Time.deltaTime;
        if (flailTimer > 0f) flailTimer -= Time.deltaTime;
    }

    private void SetEffectActive(bool active)
    {
        if (bossEffect != null && bossEffect.activeSelf != active)
        {
            bossEffect.SetActive(active);
        }
    }

    public void ChangeState(BossState newState)
    {
        if (currentState == newState && newState != BossState.Idle) return;

        currentState = newState;

        if (anim != null)
        {
            anim.SetBool(hashUsingSkill, newState == BossState.UsingSkill);
        }

        if (newState == BossState.UsingSkill)
        {
            gcdTimer = globalCooldown;
            SetChargingState(false);
            SetEffectActive(true);
        }
        else if (newState == BossState.Idle)
        {
            if (currentPhase == BossPhase.Phase1 || currentPhase == BossPhase.Phase2_5)
            {
                SetChargingState(true);
            }
            SetEffectActive(true);
        }
        else if (newState == BossState.Stunned)
        {
            SetEffectActive(false);
        }
        else if (newState == BossState.Defeated)
        {
            StopAllCoroutines();
            SetChargingState(false);
            SetEffectActive(false);
            ResetAllAnimatorParameters();

            Vector3 groundPos = transform.position;
            groundPos.y = originalPosition.y;
            transform.position = groundPos;
        }

        UpdateAnimator();
        UpdateRigState();
    }

    public void SetDefeated()
    {
        waterStack = 0;
        currentWaterZones.Clear();

        ChangeState(BossState.Defeated);

        PropController[] activeProps = FindObjectsByType<PropController>(FindObjectsSortMode.None);
        foreach (PropController prop in activeProps)
        {
            if (prop != null) prop.Drop();
        }

        ClearActiveSprinklers();
        ClearAllPropsInField();

        Debug.Log("<color=red>[Boss2Controller] 보스가 즉시 사망 상태(Defeated)로 전환되었습니다.</color>");
    }

    private void ResetAllAnimatorParameters()
    {
        if (anim == null) return;

        anim.SetBool(hashMinigun, false);
        anim.SetBool(hashMovePosition, false);
        anim.SetBool(hashCharge, false);
        anim.SetBool(hashDuringCharge, false);
        anim.SetBool(hashIsStunned, false);

        ResetSkillAnimatorTriggers();

        if (punchRig != null)
        {
            punchRig.weight = 0f;
        }

        anim.SetBool(hashIsDead, true);
    }

    private void SetChargingState(bool active)
    {
        if (!active && isCharging)
        {
            if (!_hasPlayerHitDuringCharge)
            {
                Debug.Log("<color=red>[Boss2Controller]</color> 차지 파쇄 실패! 실패 아웃풋을 실행합니다.");

                if (chargeFailDirectOutputs != null && chargeFailDirectOutputs.Count > 0)
                {
                    foreach (var output in chargeFailDirectOutputs)
                    {
                        if (output != null) output.Execute();
                    }
                }
            }
        }

        isCharging = active;
        isVulnerable = active;

        if (active)
        {
            _hasPlayerHitDuringCharge = false;

            Debug.Log("<color=yellow>[Boss2Controller]</color> 차지 준비 상태 진입!");

            if (chargePrepDirectOutputs != null && chargePrepDirectOutputs.Count > 0)
            {
                foreach (var output in chargePrepDirectOutputs)
                {
                    if (output != null) output.Execute();
                }
            }
        }

        if (anim != null)
        {
            if (active) anim.SetTrigger("ChargeTrigger");
            else anim.ResetTrigger("ChargeTrigger");
            anim.SetBool(hashCharge, active);
        }

        UpdateRigState();

        Vector3 targetPos = transform.position;
        targetPos.y = active ? (originalPosition.y - lowerAmount) : originalPosition.y;

        StartMoveToPosition(targetPos);
    }

    private void UpdateRigState()
    {
        if (rigController == null) return;

        bool enableTracking = (currentState == BossState.Idle) && !isCharging;

        rigController.SetRigWeight(enableTracking);
    }

    private void SetRigTarget(Transform target)
    {
        if (rigController == null) return;

        var targetField = rigController.GetType().GetField("playerTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (targetField != null)
        {
            targetField.SetValue(rigController, target);
        }
    }

    private void UpdateAnimator()
    {
        if (anim == null) return;

        int animatorPhaseValue = GetAnimatorPhaseValue(currentPhase);

        if (anim.GetInteger(hashPhase) != animatorPhaseValue)
        {
            anim.SetInteger(hashPhase, animatorPhaseValue);
        }

        bool isStunnedState = (currentState == BossState.Stunned);
        if (anim.GetBool(hashIsStunned) != isStunnedState)
        {
            anim.SetBool(hashIsStunned, isStunnedState);
        }

        bool isDeadState = (currentState == BossState.Defeated);
        if (anim.GetBool(hashIsDead) != isDeadState)
        {
            anim.SetBool(hashIsDead, isDeadState);
        }

        // [추가] isDebug 상태를 애니메이터 파라미터에 동기화
        if (anim.GetBool(hashDebug) != isDebug)
        {
            anim.SetBool(hashDebug, isDebug);
        }
    }

    private int GetAnimatorPhaseValue(BossPhase phase)
    {
        if (isDebug) return 0; // 디버그 모드일 때는 Phase 조건 상충 방지

        switch (phase)
        {
            case BossPhase.Phase1: return 1;
            case BossPhase.Phase2: return 2;
            case BossPhase.Phase2_5: return 3;
            case BossPhase.Phase3: return 4;
            default: return 1;
        }
    }

    private void TriggerPlayerLose()
    {
        SetChargingState(false);
        ChangeState(BossState.UsingSkill);
        Debug.Log("<color=red>[Boss2Controller] 배터리 100% 충전 완료! 플레이어 패배(Lose) 처리!</color>");
    }

    private IEnumerator AILoopRoutine()
    {
        while (currentState != BossState.Defeated)
        {
            if (isDebug || !enableAI)
            {
                yield return null;
                continue;
            }

            if (currentState == BossState.Idle)
            {
                if (!phaseSpawnExecuted)
                {
                    phaseSpawnExecuted = true;
                    SetChargingState(false);
                    yield return StartCoroutine(ExecuteSpawnPropsSkill());
                    yield return null;
                    continue;
                }

                if (currentPhase == BossPhase.Phase1)
                {
                    yield return StartCoroutine(ProcessPhase1AI());
                }
                else
                {
                    if (gcdTimer > 0f)
                    {
                        if (currentPhase == BossPhase.Phase2_5)
                        {
                            SetChargingState(true);
                        }
                        yield return null;
                        continue;
                    }

                    if (currentPhase == BossPhase.Phase2) yield return StartCoroutine(ProcessPhase2AI());
                    else if (currentPhase == BossPhase.Phase2_5) yield return StartCoroutine(ProcessPhase2_5AI());
                    else if (currentPhase == BossPhase.Phase3) yield return StartCoroutine(ProcessPhase3AI());
                }

                yield return null;
            }
            else
            {
                yield return null;
            }
        }
    }

    private IEnumerator ProcessPhase1AI()
    {
        Transform playerTrans = GetPlayerTransform();
        if (playerTrans == null) yield break;

        float distanceToPlayer = Vector3.Distance(SkillPivotPosition, playerTrans.position);

        if (distanceToPlayer <= skillRanges.innerRadius && fanSpinTimer <= 0f)
        {
            SetChargingState(false);
            fanSpinTimer = fanSpinCooldown;
            yield return StartCoroutine(ExecuteFanSpinSkill());
            yield break;
        }

        if (gcdTimer > 0f)
        {
            if (!isCharging)
            {
                SetChargingState(true);
            }
            yield return null;
            yield break;
        }

        if (spawnPropsTimer <= 0f)
        {
            SetChargingState(false);
            spawnPropsTimer = subSkillCooldownDuration;
            yield return StartCoroutine(ExecuteSpawnPropsSkill());
            yield break;
        }

        List<System.Func<IEnumerator>> randomCandidates = new List<System.Func<IEnumerator>>();
        if (bunshinTimer <= 0f) randomCandidates.Add(ExecuteBunshinWrapper);
        if (throwPropTimer <= 0f) randomCandidates.Add(ExecuteThrowPropWrapper);

        if (randomCandidates.Count > 0)
        {
            SetChargingState(false);
            int randomIndex = Random.Range(0, randomCandidates.Count);
            yield return StartCoroutine(randomCandidates[randomIndex]());
            yield break;
        }

        if (!isCharging)
        {
            SetChargingState(true);
        }
    }

    private IEnumerator ProcessPhase2AI()
    {
        Transform playerTrans = GetPlayerTransform();
        if (playerTrans == null) yield break;

        float distanceToPlayer = Vector3.Distance(SkillPivotPosition, playerTrans.position);

        if (sixPunchTimer <= 0f)
        {
            yield return StartCoroutine(ExecuteSixPunchSkill());
            yield break;
        }

        if (distanceToPlayer <= skillRanges.innerRadius)
        {
            if (koongTimer <= 0f)
            {
                koongTimer = koongCooldown;
                yield return StartCoroutine(ExecuteKoongSkill());
                yield break;
            }

            if (punchTimer <= 0f)
            {
                punchTimer = punchCooldown;
                yield return StartCoroutine(ExecuteTwoPunchSkill());
                yield break;
            }
        }

        if (distanceToPlayer <= skillRanges.midRadius && wooferRushTimer <= 0f)
        {
            wooferRushTimer = wooferRushCooldown;
            yield return StartCoroutine(ExecuteWooferRush());
            yield break;
        }

        if (gcdTimer <= 0f)
        {
            List<System.Func<IEnumerator>> subSkillCandidates = new List<System.Func<IEnumerator>>();

            if (bunshinTimer <= 0f) subSkillCandidates.Add(ExecuteBunshinWrapper);
            if (woofer1Timer <= 0f) subSkillCandidates.Add(ExecuteWoofer1Wrapper);
            if (throwPropTimer <= 0f) subSkillCandidates.Add(ExecuteThrowPropWrapper);

            if (subSkillCandidates.Count > 0)
            {
                gcdTimer = globalCooldown;
                int randomIndex = Random.Range(0, subSkillCandidates.Count);
                yield return StartCoroutine(subSkillCandidates[randomIndex]());
                yield break;
            }
        }

        yield return StartCoroutine(ExecuteRelocateSkillWrapper());
    }

    private IEnumerator ProcessPhase2_5AI()
    {
        if (phase2_5ChargePoint != null)
        {
            Vector3 flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 flatTarget = new Vector3(phase2_5ChargePoint.position.x, 0f, phase2_5ChargePoint.position.z);

            if (Vector3.Distance(flatCurrent, flatTarget) > 0.5f)
            {
                SetChargingState(false);
                ChangeState(BossState.UsingSkill);

                if (rigController != null) rigController.SetRigWeight(false);
                if (anim != null) anim.SetBool(hashMovePosition, true);

                yield return new WaitForSeconds(0.2f);

                Vector3 targetPos = new Vector3(phase2_5ChargePoint.position.x, transform.position.y, phase2_5ChargePoint.position.z);

                while (Vector3.Distance(flatCurrent, flatTarget) > 0.5f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, targetPos, phase2_5MoveSpeed * Time.deltaTime);
                    flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
                    yield return null;
                }

                transform.position = targetPos;

                if (anim != null) anim.SetBool(hashMovePosition, false);
                yield return new WaitForSeconds(0.3f);
            }
        }

        currentState = BossState.Idle;
        UpdateAnimator();

        SetChargingState(true);

        if (rigController != null) rigController.SetRigWeight(false);

        while (currentPhase == BossPhase.Phase2_5 && currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            yield return null;
        }
    }

    private IEnumerator ProcessPhase3AI()
    {
        Transform playerTrans = GetPlayerTransform();
        if (playerTrans == null) yield break;

        float distanceToPlayer = Vector3.Distance(SkillPivotPosition, playerTrans.position);

        if (sixPunchTimer <= 0f)
        {
            yield return StartCoroutine(ExecuteSixPunchSkill());
            yield break;
        }

        if (distanceToPlayer <= skillRanges.innerRadius)
        {
            if (koongTimer <= 0f)
            {
                koongTimer = koongCooldown;
                yield return StartCoroutine(ExecuteKoongSkill());
                yield break;
            }
        }

        if (distanceToPlayer <= skillRanges.midRadius)
        {
            if (wooferRushTimer <= 0f)
            {
                wooferRushTimer = wooferRushCooldown;
                yield return StartCoroutine(ExecuteWooferRushPhase3());
                yield break;
            }

            if (flailTimer <= 0f)
            {
                flailTimer = flailCooldown;
                yield return StartCoroutine(ExecuteFlailSwingSkill());
                yield break;
            }
        }

        if (gcdTimer <= 0f && spawnPropsTimer <= 0f)
        {
            spawnPropsTimer = subSkillCooldownDuration;
            yield return StartCoroutine(ExecuteSpawnPropsSkill());
            yield break;
        }

        if (gcdTimer <= 0f && phase3SubSkillTimer <= 0f)
        {
            List<System.Func<IEnumerator>> subSkillCandidates = new List<System.Func<IEnumerator>>
            {
                ExecuteChargeAndBoomWrapper,
                ExecuteWoofer1Wrapper,
                ExecuteChargeAndCrashWrapper
            };

            phase3SubSkillTimer = phase3SubSkillCooldown;

            int randomIndex = Random.Range(0, subSkillCandidates.Count);
            yield return StartCoroutine(subSkillCandidates[randomIndex]());
            yield break;
        }

        yield return StartCoroutine(ExecuteRelocateSkillWrapper());
    }

    private IEnumerator WaitUntilAnimationFinished(int stateHash, float maxTimeout = 3.0f)
    {
        if (anim == null) yield break;

        yield return new WaitForEndOfFrame();

        float elapsed = 0f;

        while (elapsed < maxTimeout && !anim.GetCurrentAnimatorStateInfo(0).shortNameHash.Equals(stateHash))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        while (elapsed < maxTimeout)
        {
            elapsed += Time.deltaTime;
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);

            if (!anim.IsInTransition(0) && info.normalizedTime >= 0.95f)
            {
                break;
            }
            yield return null;
        }
    }

    private Transform PrepareSkillFacing()
    {
        if (rigController != null)
        {
            rigController.SetRigWeight(false);
        }

        Transform pTrans = GetPlayerTransform();
        if (pTrans != null)
        {
            Vector3 dirToPlayer = (pTrans.position - transform.position);
            dirToPlayer.y = 0f;

            if (dirToPlayer.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToPlayer.normalized, Vector3.up);
                transform.rotation = targetRot;

                var bossRootField = rigController?.GetType().GetField("bossRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rigController) as Transform;
                if (bossRootField != null)
                {
                    bossRootField.rotation = targetRot;
                }
            }
        }

        return pTrans;
    }

    private void CheckAndBlastProps(Vector3 centerPos, float radius, float blastForce)
    {
        Collider[] hits = Physics.OverlapSphere(centerPos, radius, CombinedTargetLayer);
        foreach (Collider col in hits)
        {
            PropController prop = col.GetComponent<PropController>() ?? col.GetComponentInParent<PropController>();
            if (prop != null)
            {
                prop.BlastAway(centerPos, blastForce);
            }
        }
    }

    #region Skill: GarenE (회전 스킬 애니메이션 미리보기용 구현)
    /// <summary>
    /// GarenE 스킬 실행 루틴. 현재는 상태 변경, 트리거 호출 및 애니메이션 확인을 위한 코루틴 대기 로직만 포함되어 있습니다.
    /// </summary>
    public IEnumerator ExecuteGarenESkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("GarenE");

        garenETimer = garenECooldown;

        if (rigController != null)
        {
            rigController.SetRigWeight(true);
        }

        if (anim != null)
        {
            anim.ResetTrigger(hashGarenE);
            anim.SetTrigger(hashGarenE);
        }

        // 애니메이션 재생 유지를 확인하는 대기 로직 (필요 시 애니메이션 길이에 맞춰 조정 가능)
        yield return StartCoroutine(WaitUntilAnimationFinished(hashGarenE, 3.0f));

        yield return new WaitForSeconds(0.5f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }
    #endregion

    #region New Skill Implementations (Two Punch & Flail Swing)
    public IEnumerator ExecuteTwoPunchSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("TwoPunch");

        Transform pTrans = PrepareSkillFacing();

        if (pTrans != null && punchRigTarget != null)
        {
            Vector3 dirToPlayer = (pTrans.position - transform.position);
            dirToPlayer.y = 0f;
            float ikDistance = Mathf.Min(dirToPlayer.magnitude, 15.0f);

            punchRigTarget.position = transform.position + dirToPlayer.normalized * ikDistance + Vector3.up * 1.2f;
            punchRigTarget.rotation = transform.rotation;
        }

        yield return StartCoroutine(SetPunchRigWeight(1.0f));

        Vector3 punch1Pos = transform.position + transform.forward * 60.0f;
        SkillWarningIndicator.SpawnCircle(punch1Pos + transform.forward * punchStepDistance, 80.0f, punchPrepTime, new Color(1f, 0.2f, 0.1f, 0.4f), groundLayer);

        yield return new WaitForSeconds(punchPrepTime);

        EnablePunchHitboxes(true);

        if (anim != null)
        {
            anim.ResetTrigger(hashPunch1);
            anim.SetTrigger(hashPunch1);
        }
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

        yield return StartCoroutine(PunchStepForward(punchStepDistance, GetPlayerTransform()));

        punch1Pos = transform.position + transform.forward * 60.0f;
        CheckAndBlastProps(punch1Pos, 80.0f, 100f);
        ApplyPunchKnockback(punch1Pos, 80.0f, punchKnockbackForce);

        yield return StartCoroutine(WaitUntilAnimationFinished(hashPunch1, punchStateFallbackDuration));

        EnablePunchHitboxes(false);

        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

        pTrans = GetPlayerTransform();
        if (pTrans != null && punchRigTarget != null)
        {
            Vector3 dirToPlayer = (pTrans.position - transform.position);
            dirToPlayer.y = 0f;
            float ikDistance = Mathf.Min(dirToPlayer.magnitude, 15.0f);

            punchRigTarget.position = transform.position + dirToPlayer.normalized * ikDistance + Vector3.up * 1.2f;
            punchRigTarget.rotation = transform.rotation;
        }

        float punch2PrepTime = 0.3f;
        Vector3 punch2Pos = transform.position + transform.forward * 60.0f;
        SkillWarningIndicator.SpawnCircle(punch2Pos + transform.forward * punchStepDistance, 80.0f, punch2PrepTime, new Color(1f, 0.2f, 0.1f, 0.4f), groundLayer);

        yield return new WaitForSeconds(punch2PrepTime);

        EnablePunchHitboxes(true);

        if (anim != null)
        {
            anim.ResetTrigger(hashPunch2);
            anim.SetTrigger(hashPunch2);
        }
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

        yield return StartCoroutine(PunchStepForward(punchStepDistance, GetPlayerTransform()));

        punch2Pos = transform.position + transform.forward * 60.0f;
        CheckAndBlastProps(punch2Pos, 80.0f, 100f);
        ApplyPunchKnockback(punch2Pos, 80.0f, punchKnockbackForce);

        yield return StartCoroutine(WaitUntilAnimationFinished(hashPunch2, punchStateFallbackDuration));

        EnablePunchHitboxes(false);

        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

        yield return StartCoroutine(SetPunchRigWeight(0.0f));

        yield return new WaitForSeconds(0.2f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void EnablePunchHitboxes(bool enable)
    {
        if (punchHitboxes == null) return;
        foreach (var hb in punchHitboxes)
        {
            if (hb == null) continue;
            if (enable) hb.ActivateHitbox(punchKnockbackForce);
            else hb.DeactivateHitbox();
        }
    }

    private void EnableFlailHitboxes(bool enable)
    {
        if (flailHitboxes == null) return;
        foreach (var hb in flailHitboxes)
        {
            if (hb == null) continue;
            if (enable) hb.ActivateHitbox(flailBlastForce);
            else hb.DeactivateHitbox();
        }
    }

    private void ApplyPunchKnockback(Vector3 center, float radius, float force)
    {
        Collider[] hits = Physics.OverlapSphere(center, radius, CombinedTargetLayer);
        bool hitPlayer = false;

        foreach (Collider col in hits)
        {
            if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper") || col.CompareTag("Player"))
            {
                hitPlayer = true;
            }
            TryApplyKnockback(col, force, applyUpwardForce: true, applyDamage: true);
        }

        if (!hitPlayer)
        {
            GameObject targetPlayer = bumperObj != null ? bumperObj : playerObj;
            if (targetPlayer != null)
            {
                float dist = Vector3.Distance(center, targetPlayer.transform.position);
                if (dist <= radius + 3.0f)
                {
                    Collider pCol = targetPlayer.GetComponentInChildren<Collider>();
                    if (pCol != null)
                    {
                        TryApplyKnockback(pCol, force, applyUpwardForce: true, applyDamage: true);
                    }
                }
            }
        }
    }

    private IEnumerator PunchStepForward(float distance, Transform target)
    {
        Vector3 startPos = transform.position;
        float traveledDistance = 0f;

        while (traveledDistance < distance)
        {
            Vector3 currentMoveDir = transform.forward;
            if (target != null)
            {
                Vector3 dirToTarget = (target.position - transform.position);
                dirToTarget.y = 0f;

                if (dirToTarget.sqrMagnitude > 0.001f)
                {
                    currentMoveDir = dirToTarget.normalized;
                }
            }

            Quaternion targetRotation = Quaternion.LookRotation(currentMoveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, punchRotationSpeed * Time.deltaTime);

            if (punchRigTarget != null && target != null)
            {
                punchRigTarget.position = target.position + Vector3.up * 1.2f;
                punchRigTarget.rotation = targetRotation;
            }

            float moveStep = punchDashSpeed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, transform.position + transform.forward * distance, moveStep);
            traveledDistance += moveStep;

            Transform pTrans = GetPlayerTransform();
            if (pTrans != null)
            {
                Vector3 pushDiff = pTrans.position - transform.position;
                pushDiff.y = 0f;
                if (pushDiff.magnitude < 7.0f)
                {
                    Vector3 pushDir = pushDiff.sqrMagnitude < 0.01f ? transform.forward : pushDiff.normalized;
                    Rigidbody pRb = pTrans.GetComponentInParent<Rigidbody>() ?? pTrans.GetComponent<Rigidbody>();
                    if (pRb != null)
                    {
                        pRb.linearVelocity = pushDir * punchKnockbackForce * 0.6f + Vector3.up * 2f;
                    }
                }
            }

            yield return null;
        }
    }

    private IEnumerator SetPunchRigWeight(float targetWeight)
    {
        if (punchRig == null) yield break;

        while (!Mathf.Approximately(punchRig.weight, targetWeight))
        {
            punchRig.weight = Mathf.MoveTowards(punchRig.weight, targetWeight, punchRigWeightSpeed * Time.deltaTime);
            yield return null;
        }
    }

    public IEnumerator ExecuteFlailSwingSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("FlailSwing");

        if (skillEffectOutput != null)
        {
            skillEffectOutput.TriggerOutputNodes(_Project.Scripts.VisualScripting.BossSkillType.FlailSwing);
        }

        Transform pTrans = PrepareSkillFacing();

        float flailImpactDistance = 100.0f;
        float flailImpactRadius = 100.0f;
        Vector3 flailImpactPos = transform.position + transform.forward * flailImpactDistance;

        float flailPrepTime = 0.6f;
        SkillWarningIndicator.SpawnCircle(flailImpactPos, flailImpactRadius, flailPrepTime, new Color(1f, 0.2f, 0.1f, 0.4f), groundLayer);

        yield return new WaitForSeconds(flailPrepTime);

        EnableFlailHitboxes(true);

        if (anim != null)
        {
            anim.ResetTrigger(hashFlailSwing);
            anim.SetTrigger(hashFlailSwing);
        }

        yield return StartCoroutine(WaitUntilAnimationFinished(hashFlailSwing, 1.5f));

        EnableFlailHitboxes(false);

        yield return new WaitForSeconds(1.0f);

        flailImpactPos = transform.position + transform.forward * flailImpactDistance;

        if (skillEffectOutput != null)
        {
            skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.FlailSwing, flailImpactPos, triggerOutputs: false);
        }

        CheckAndBlastProps(flailImpactPos, flailImpactRadius, flailBlastForce);
        ApplyFlailKnockback(flailImpactPos, flailImpactRadius, flailBlastForce);

        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.FlailSwing);

        yield return new WaitForSeconds(1.5f);

        if (skillEffectOutput != null)
        {
            skillEffectOutput.StopEffect(_Project.Scripts.VisualScripting.BossSkillType.FlailSwing);
        }

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void ApplyFlailKnockback(Vector3 center, float radius, float force)
    {
        Collider[] hits = Physics.OverlapSphere(center, radius, CombinedTargetLayer);
        bool hitPlayer = false;

        foreach (Collider col in hits)
        {
            if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper") || col.CompareTag("Player"))
            {
                hitPlayer = true;
            }
            TryApplyKnockback(col, force, applyUpwardForce: true, applyDamage: true);
        }

        if (!hitPlayer)
        {
            GameObject targetPlayer = bumperObj != null ? bumperObj : playerObj;
            if (targetPlayer != null)
            {
                float dist = Vector3.Distance(center, targetPlayer.transform.position);
                if (dist <= radius + 3.0f)
                {
                    Collider pCol = targetPlayer.GetComponentInChildren<Collider>();
                    if (pCol != null)
                    {
                        TryApplyKnockback(pCol, force, applyUpwardForce: true, applyDamage: true);
                    }
                }
            }
        }
    }
    #endregion

    private IEnumerator ExecuteChargeAndBoomWrapper()
    {
        yield return StartCoroutine(ExecutePullAndPushSkill());
    }

    private IEnumerator ExecuteChargeAndCrashWrapper()
    {
        yield return StartCoroutine(ExecuteThrowHeavyPropSkill());
    }

    private IEnumerator ExecuteWooferRushPhase3()
    {
        float originalSpeed = wooferRushSpeed;
        wooferRushSpeed *= phase3WooferRushSpeedMultiplier;

        yield return StartCoroutine(ExecuteWooferRush());

        wooferRushSpeed = originalSpeed;
    }

    private IEnumerator ExecuteWoofer1Wrapper()
    {
        woofer1Timer = woofer1Cooldown;
        yield return StartCoroutine(ExecuteWoofer1Skill());
    }

    private IEnumerator ExecuteBunshinWrapper()
    {
        bunshinTimer = subSkillCooldownDuration;
        yield return StartCoroutine(ExecuteBunshinAndStraightShotSkill());
    }

    private IEnumerator ExecuteThrowPropWrapper()
    {
        throwPropTimer = subSkillCooldownDuration;
        yield return StartCoroutine(ExecuteThrowPropSkill());
    }

    public IEnumerator ExecutePullAndPushSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("ChargeAndBoom");
        Vector3 pivotPos = SkillPivotPosition;
        SkillWarningIndicator.SpawnCircle(pivotPos, skillRanges.midRadius, holdDuration + 0.9f, new Color(0.6f, 0.1f, 0.8f, 0.35f), groundLayer);

        if (anim != null) anim.SetTrigger(hashChargeAndBoom);
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.PullAndPush);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.PullAndPush);

        yield return new WaitForSeconds(0.5f);

        HashSet<PropController> pulledProps = new HashSet<PropController>();
        float elapsedTime = 0f;

        while (elapsedTime < holdDuration)
        {
            elapsedTime += Time.fixedDeltaTime;

            Collider[] hitColliders = Physics.OverlapSphere(pivotPos, skillRanges.midRadius, CombinedTargetLayer);

            foreach (Collider col in hitColliders)
            {
                PropController prop = col.GetComponent<PropController>();
                if (prop != null && !pulledProps.Contains(prop))
                {
                    prop.StartPull(pivotPos, holdDuration, lightPushSpeed);
                    pulledProps.Add(prop);
                }

                if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
                {
                    Rigidbody targetRb = col.GetComponentInParent<Rigidbody>() ?? col.GetComponent<Rigidbody>();

                    if (targetRb != null)
                    {
                        Vector3 pullDir = (pivotPos - col.transform.position);
                        pullDir.y = 0f;

                        if (pullDir.sqrMagnitude > 1.0f)
                        {
                            pullDir.Normalize();
                            targetRb.AddForce(pullDir * 35f, ForceMode.Acceleration);
                        }
                    }
                }
            }

            yield return new WaitForFixedUpdate();
        }

        if (anim != null) anim.SetTrigger(hashWooperAttack);

        yield return new WaitForSeconds(0.4f);

        foreach (PropController prop in pulledProps)
        {
            if (prop != null)
            {
                Vector3 throwDir = (prop.transform.position - transform.position);
                throwDir.y = 0f;

                if (throwDir.sqrMagnitude < 0.001f) throwDir = transform.forward;
                throwDir.Normalize();

                prop.ThrowAt(throwDir, lightPushSpeed);
            }
        }
        pulledProps.Clear();

        ApplyWooferKnockback(skillRanges.midRadius, false);

        yield return new WaitForSeconds(1.0f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    public IEnumerator ExecuteKoongSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteKoongSkill));

        if (rigController != null)
        {
            rigController.SetRigWeight(true);
        }

        float baseGroundY = transform.position.y;
        if (Physics.Raycast(transform.position + Vector3.up * 2.0f, Vector3.down, out RaycastHit initGroundHit, 10.0f, groundLayer))
        {
            baseGroundY = initGroundHit.point.y;
        }
        else
        {
            baseGroundY = originalPosition.y;
        }

        if (anim != null) anim.SetTrigger(hashKoong);

        Vector3 startPos = transform.position;
        Vector3 airPos = new Vector3(startPos.x, baseGroundY + koongJumpHeight, startPos.z);

        float moveTimeout = 0f;
        while (Vector3.Distance(transform.position, airPos) > 0.1f && moveTimeout < 2.0f)
        {
            moveTimeout += Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, airPos, koongMoveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = airPos;

        float followTimer = 0f;
        Transform playerTrans = GetPlayerTransform();

        while (followTimer < koongFollowDuration)
        {
            followTimer += Time.deltaTime;
            if (playerTrans == null) playerTrans = GetPlayerTransform();

            if (playerTrans != null)
            {
                Vector3 targetAirPos = new Vector3(playerTrans.position.x, airPos.y, playerTrans.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetAirPos, koongMoveSpeed * Time.deltaTime);
            }
            yield return null;
        }

        float targetGroundY = baseGroundY;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit groundHit, koongJumpHeight * 2.0f, groundLayer))
        {
            targetGroundY = groundHit.point.y;
        }

        Vector3 targetGroundPos = new Vector3(transform.position.x, targetGroundY, transform.position.z);
        SkillWarningIndicator.SpawnCircle(targetGroundPos, koongImpactRadius, koongAirHoverTime, new Color(1f, 0f, 0f, 0.45f), groundLayer);

        yield return new WaitForSeconds(koongAirHoverTime);

        if (rigController != null) rigController.SetRigWeight(false);

        Vector3 plungePos = new Vector3(transform.position.x, targetGroundY - koongOvershootDepth, transform.position.z);

        moveTimeout = 0f;
        while (Vector3.Distance(transform.position, plungePos) > 0.1f && moveTimeout < 1.5f)
        {
            moveTimeout += Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, plungePos, koongMoveSpeed * 3.5f * Time.deltaTime);
            yield return null;
        }
        transform.position = plungePos;

        if (anim != null) anim.SetTrigger(hashKoongAfter);
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.KoongSkill);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.KoongSkill);

        ApplyKoongImpact();

        float finalTargetY = isCharging ? (originalPosition.y - lowerAmount) : originalPosition.y;
        Vector3 finalLandPos = new Vector3(transform.position.x, finalTargetY, transform.position.z);

        moveTimeout = 0f;
        while (Vector3.Distance(transform.position, finalLandPos) > 0.05f && moveTimeout < 1.0f)
        {
            moveTimeout += Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, finalLandPos, koongReturnSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = finalLandPos;

        yield return new WaitForSeconds(0.3f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void ApplyKoongImpact()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, koongImpactRadius, CombinedTargetLayer);
        bool hitPlayer = false;

        foreach (Collider col in hitColliders)
        {
            if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper") || col.CompareTag("Player"))
            {
                hitPlayer = true;

                KartEnvironmentalHazard hazard = col.GetComponentInParent<KartEnvironmentalHazard>()
                    ?? col.GetComponent<KartEnvironmentalHazard>()
                    ?? col.GetComponentInChildren<KartEnvironmentalHazard>();
                if (hazard != null)
                {
                    hazard.TakeDamage(1);
                }

                Rigidbody targetRb = col.GetComponentInParent<Rigidbody>() ?? col.GetComponent<Rigidbody>();
                if (targetRb != null)
                {
                    Vector3 knockbackDir = col.transform.position - transform.position;
                    knockbackDir.y = 0f;

                    if (knockbackDir.sqrMagnitude < 0.01f) knockbackDir = transform.forward;
                    knockbackDir.Normalize();

                    Vector3 safePos = transform.position + knockbackDir * (koongImpactRadius * 0.4f);
                    safePos.y = col.transform.position.y + 0.3f;
                    col.transform.position = safePos;

                    knockbackDir.y = 0.35f;
                    knockbackDir.Normalize();

                    targetRb.linearVelocity = Vector3.zero;
                    targetRb.angularVelocity = Vector3.zero;
                    targetRb.linearVelocity = knockbackDir * koongKnockbackForce;
                }
            }
        }

        if (!hitPlayer)
        {
            GameObject targetPlayer = bumperObj != null ? bumperObj : playerObj;
            if (targetPlayer != null)
            {
                float dist = Vector3.Distance(transform.position, targetPlayer.transform.position);
                if (dist <= koongImpactRadius)
                {
                    Collider pCol = targetPlayer.GetComponentInChildren<Collider>();
                    if (pCol != null)
                    {
                        TryApplyKnockback(pCol, koongKnockbackForce, applyUpwardForce: true, applyDamage: true);
                    }
                }
            }
        }
    }

    private void UpdateBattery()
    {
        if (currentState == BossState.Stunned || currentState == BossState.Defeated) return;

        float previousBattery = currentBattery;

        if (currentPhase == BossPhase.Phase1 || currentPhase == BossPhase.Phase2_5)
        {
            if (isCharging && currentState == BossState.Idle)
            {
                currentBattery += batteryChargeRate * Time.deltaTime;
                currentBattery = Mathf.Clamp(currentBattery, 0f, maxBattery);

                if (currentBattery >= maxBattery)
                {
                    TriggerPlayerLose();
                }
            }
        }
        else if (currentPhase == BossPhase.Phase2)
        {
            currentBattery -= batteryDrainRate * Time.deltaTime;
            currentBattery = Mathf.Clamp(currentBattery, 0f, maxBattery);

            if (currentBattery <= 10f)
            {
                Debug.Log("<color=yellow>[Boss2Controller] 배터리 10% 남음! 2.5페이즈로 전환합니다.</color>");
                currentPhase = BossPhase.Phase2_5;
                UpdateAnimator();

                ChangeState(BossState.Idle);
            }
        }

        if (!Mathf.Approximately(previousBattery, currentBattery))
        {
            OnBatteryChanged?.Invoke(currentBattery, maxBattery);
        }
    }

    public IEnumerator ExecuteBunshinAndStraightShotSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteBunshinAndStraightShotSkill));

        if (anim != null) anim.SetBool(hashMinigun, true);
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.BunshinCombo);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.BunshinCombo);

        Transform targetTransform = GetPlayerTransform();
        Transform bunshinSpawn = (bunshinSpawnPoint != null) ? bunshinSpawnPoint :
                                 (straightShotSpawnPoint != null) ? straightShotSpawnPoint : transform;

        int currentBunshinShotCount = 0;

        for (int i = 0; i < totalBunshinCount; i++)
        {
            if (bunshinPrefab != null)
            {
                Vector3 spawnPos = bunshinSpawn.position;
                Vector3 targetPos = (targetTransform != null) ? targetTransform.position : (transform.position + transform.forward * 20f);
                Vector3 baseDir = (targetPos - spawnPos).normalized;

                Vector3 spreadOffset = Random.insideUnitSphere * Mathf.Sin(spreadAngle * Mathf.Deg2Rad);
                Vector3 shootDir = (baseDir + spreadOffset).normalized;

                GameObject spawnedBunshin = Instantiate(bunshinPrefab, spawnPos, Quaternion.identity);

                ShabulkabunshinLocomotion bunshinLoco = spawnedBunshin.GetComponent<ShabulkabunshinLocomotion>();
                if (bunshinLoco != null)
                {
                    bunshinLoco.Launch(targetTransform, shootDir);
                }

                Rigidbody bunshinRb = spawnedBunshin.GetComponent<Rigidbody>();
                if (bunshinRb != null)
                {
                    bunshinRb.linearVelocity = shootDir * bunshinShootSpeed;
                }
            }

            currentBunshinShotCount++;

            if (bunshinIntervalForStraightShot > 0 &&
                currentBunshinShotCount % bunshinIntervalForStraightShot == 0 &&
                straightShotPrefabs != null && straightShotPrefabs.Count > 0)
            {
                StartCoroutine(FireStraightShotRoutine(targetTransform));
            }

            yield return new WaitForSeconds(fireInterval);
        }

        if (anim != null) anim.SetBool(hashMinigun, false);

        yield return new WaitForSeconds(0.8f);
        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void CheckFlailColliderHits(HashSet<PropController> hitProps)
    {
        if (flailColliders == null || flailColliders.Count == 0) return;

        foreach (Collider flailCol in flailColliders)
        {
            if (flailCol == null || !flailCol.enabled) continue;

            Collider[] hits = Physics.OverlapBox(
                flailCol.bounds.center,
                flailCol.bounds.extents,
                flailCol.transform.rotation,
                CombinedTargetLayer
            );

            foreach (Collider col in hits)
            {
                PropController prop = col.GetComponent<PropController>() ?? col.GetComponentInParent<PropController>();
                if (prop != null && !hitProps.Contains(prop))
                {
                    hitProps.Add(prop);
                    prop.BlastAway(flailCol.bounds.center, flailBlastForce);
                }

                TryApplyKnockback(col, flailBlastForce, applyUpwardForce: true, applyDamage: true);
            }

            GameObject targetPlayer = bumperObj != null ? bumperObj : playerObj;
            if (targetPlayer != null)
            {
                Collider pCol = targetPlayer.GetComponentInChildren<Collider>();
                if (pCol != null && flailCol.bounds.Intersects(pCol.bounds))
                {
                    TryApplyKnockback(pCol, flailBlastForce, applyUpwardForce: true, applyDamage: true);
                }
            }
        }
    }

    private IEnumerator FireStraightShotRoutine(Transform targetTransform)
    {
        Vector3 propSpawnPos = (straightShotSpawnPoint != null) ? straightShotSpawnPoint.position : transform.position + propSpawnOffset;

        if (anim != null) anim.SetTrigger(hashDropProp);

        GameObject selectedPrefab = straightShotPrefabs[Random.Range(0, straightShotPrefabs.Count)];
        GameObject spawnedProp = Instantiate(selectedPrefab, propSpawnPos, Quaternion.identity);

        PropController propCtrl = spawnedProp.GetComponent<PropController>();
        if (propCtrl == null) propCtrl = spawnedProp.AddComponent<PropController>();

        Vector3 propTargetPos = (targetTransform != null) ? targetTransform.position : (transform.position + transform.forward * 20f);
        Vector3 shootDir = (propTargetPos - propSpawnPos);
        if (shootDir.sqrMagnitude < 0.001f) shootDir = transform.forward;
        shootDir.Normalize();

        spawnedProp.transform.rotation = Quaternion.LookRotation(shootDir);

        SkillWarningIndicator.SpawnBox(propSpawnPos, shootDir, 4.0f, 200.0f, throwReleaseDelay, new Color(1f, 0.4f, 0f, 0.35f), groundLayer);

        yield return new WaitForSeconds(throwReleaseDelay);

        if (spawnedProp != null && propCtrl != null)
        {
            propCtrl.ThrowStraightAt(shootDir, straightShotSpeed);
        }
    }

    private void MarkSkillUsed(string skillName)
    {
        usedSkillsInCurrentPhase.Add(skillName);
    }

    private IEnumerator ExecuteRelocateSkillWrapper()
    {
        MarkSkillUsed(nameof(ExecuteRelocateSkillWrapper));
        yield return StartCoroutine(ExecuteRelocateRoutine());
    }

    public IEnumerator ExecuteRelocateRoutine(Transform customTarget = null, float? customAngle = null)
    {
        ChangeState(BossState.UsingSkill);

        if (anim != null) anim.SetBool(hashMovePosition, true);
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.Relocate);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.Relocate);

        Transform target = customTarget != null ? customTarget : (moveCenterTarget != null ? moveCenterTarget : GetPlayerTransform());

        if (target == null)
        {
            Debug.LogWarning("[Boss2Controller] Relocate 타겟을 찾을 수 없습니다.");
            if (anim != null) anim.SetBool(hashMovePosition, false);
            ChangeState(BossState.Idle);
            yield break;
        }

        float currentY = transform.position.y;
        Vector3 calculatedTargetPos;

        if (customAngle.HasValue)
        {
            float rad = customAngle.Value * Mathf.Deg2Rad;
            calculatedTargetPos = new Vector3(
                target.position.x + Mathf.Cos(rad) * moveAroundRadius,
                currentY,
                target.position.z + Mathf.Sin(rad) * moveAroundRadius
            );
        }
        else
        {
            Vector3 dirToPlayer = (target.position - transform.position);
            dirToPlayer.y = 0f;

            float baseAngle = Mathf.Atan2(dirToPlayer.z, dirToPlayer.x) * Mathf.Rad2Deg;
            float randomOffset = Random.Range(-45f, 45f);
            float finalAngleRad = (baseAngle + randomOffset) * Mathf.Deg2Rad;

            calculatedTargetPos = new Vector3(
                target.position.x - Mathf.Cos(finalAngleRad) * moveAroundRadius,
                currentY,
                target.position.z - Mathf.Sin(finalAngleRad) * moveAroundRadius
            );
        }

        Vector3 startPos = transform.position;
        Vector3 moveDir = calculatedTargetPos - startPos;
        moveDir.y = 0f;
        float moveDistance = moveDir.magnitude;

        if (moveDistance > 0.001f)
        {
            moveDir.Normalize();
            if (Physics.SphereCast(startPos, moveObstacleCheckRadius, moveDir, out RaycastHit wallHit, moveDistance, groundLayer))
            {
                float safeDistance = Mathf.Max(0f, wallHit.distance - 2.0f);
                calculatedTargetPos = startPos + moveDir * safeDistance;
                calculatedTargetPos.y = currentY;
            }
        }

        Vector3 flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(calculatedTargetPos.x, 0f, calculatedTargetPos.z);

        while (Vector3.Distance(flatCurrent, flatTarget) > 0.1f)
        {
            Vector3 currentDir = calculatedTargetPos - transform.position;
            currentDir.y = 0f;
            float remainingDist = currentDir.magnitude;

            if (remainingDist > 0.001f)
            {
                currentDir.Normalize();
                if (Physics.SphereCast(transform.position, moveObstacleCheckRadius, currentDir, out RaycastHit hit, 1.5f, groundLayer))
                {
                    Debug.Log($"[Boss2Controller] 이동 중 Ground 벽({hit.collider.name}) 감지! 멈춤.");
                    break;
                }
            }

            transform.position = Vector3.MoveTowards(transform.position, calculatedTargetPos, relocationSpeed * Time.deltaTime);

            flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
            yield return null;
        }

        if (anim != null) anim.SetBool(hashMovePosition, false);

        yield return new WaitForSeconds(0.4f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    #region Skill: Continuous 6-Punch Combo (2-Punch x 3)
    public IEnumerator ExecuteSixPunchSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("SixPunch");

        sixPunchTimer = sixPunchCooldown;

        yield return StartCoroutine(SetPunchRigWeight(1.0f));

        for (int comboCount = 0; comboCount < 3; comboCount++)
        {
            Transform pTrans = PrepareSkillFacing();

            if (pTrans != null && punchRigTarget != null)
            {
                Vector3 dirToPlayer = (pTrans.position - transform.position);
                dirToPlayer.y = 0f;
                float ikDistance = Mathf.Min(dirToPlayer.magnitude, 15.0f);

                punchRigTarget.position = transform.position + dirToPlayer.normalized * ikDistance + Vector3.up * 1.2f;
                punchRigTarget.rotation = transform.rotation;
            }

            Vector3 punch1Pos = transform.position + transform.forward * 60.0f;
            SkillWarningIndicator.SpawnCircle(punch1Pos + transform.forward * punchStepDistance, 80.0f, punchPrepTime, new Color(1f, 0.2f, 0.1f, 0.4f), groundLayer);

            yield return new WaitForSeconds(punchPrepTime);

            EnablePunchHitboxes(true);

            if (anim != null)
            {
                anim.ResetTrigger(hashPunch1);
                anim.SetTrigger(hashPunch1);
            }
            if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

            yield return StartCoroutine(PunchStepForward(punchStepDistance, GetPlayerTransform()));

            punch1Pos = transform.position + transform.forward * 60.0f;
            CheckAndBlastProps(punch1Pos, 80.0f, 100f);
            ApplyPunchKnockback(punch1Pos, 80.0f, punchKnockbackForce);

            yield return StartCoroutine(WaitUntilAnimationFinished(hashPunch1, punchStateFallbackDuration));

            EnablePunchHitboxes(false);
            if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);


            pTrans = PrepareSkillFacing();
            if (pTrans != null && punchRigTarget != null)
            {
                Vector3 dirToPlayer = (pTrans.position - transform.position);
                dirToPlayer.y = 0f;
                float ikDistance = Mathf.Min(dirToPlayer.magnitude, 15.0f);

                punchRigTarget.position = transform.position + dirToPlayer.normalized * ikDistance + Vector3.up * 1.2f;
                punchRigTarget.rotation = transform.rotation;
            }

            float punch2PrepTime = 0.25f;
            Vector3 punch2Pos = transform.position + transform.forward * 60.0f;
            SkillWarningIndicator.SpawnCircle(punch2Pos + transform.forward * punchStepDistance, 80.0f, punch2PrepTime, new Color(1f, 0.2f, 0.1f, 0.4f), groundLayer);

            yield return new WaitForSeconds(punch2PrepTime);

            EnablePunchHitboxes(true);

            if (anim != null)
            {
                anim.ResetTrigger(hashPunch2);
                anim.SetTrigger(hashPunch2);
            }
            if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

            yield return StartCoroutine(PunchStepForward(punchStepDistance, GetPlayerTransform()));

            punch2Pos = transform.position + transform.forward * 60.0f;
            CheckAndBlastProps(punch2Pos, 80.0f, 100f);
            ApplyPunchKnockback(punch2Pos, 80.0f, punchKnockbackForce);

            yield return StartCoroutine(WaitUntilAnimationFinished(hashPunch2, punchStateFallbackDuration));

            EnablePunchHitboxes(false);
            if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.TwoPunch);

            if (currentState == BossState.Stunned || currentState == BossState.Defeated)
            {
                yield break;
            }
        }

        yield return StartCoroutine(SetPunchRigWeight(0.0f));
        yield return new WaitForSeconds(0.2f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }
    #endregion

    public IEnumerator ExecuteChargeAttackSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteChargeAttackSkill));

        Vector3 loweredPos = new Vector3(transform.position.x, originalPosition.y - lowerAmount, transform.position.z);
        yield return StartCoroutine(MoveToPositionRoutine(loweredPos));

        Transform actualModelTransform = visualRoot;
        if (actualModelTransform == null && rigController != null)
        {
            actualModelTransform = rigController.GetType()
                .GetField("bossRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(rigController) as Transform;
        }
        if (actualModelTransform == null) actualModelTransform = transform;

        Vector3 chargeDir = actualModelTransform.forward;
        chargeDir.y = 0f;
        chargeDir.Normalize();

        SkillWarningIndicator.SpawnBox(transform.position, chargeDir, castRadius * 2f, chargeDistance, chargePrepTime, new Color(1f, 0.5f, 0f, 0.4f), groundLayer);

        yield return new WaitForSeconds(chargePrepTime);

        Vector3 localToWorldOffset = transform.TransformDirection(sphereCastOffset);
        Vector3 startPos = transform.position + localToWorldOffset;

        Vector3 targetPos = transform.position + chargeDir * chargeDistance;

        if (Physics.SphereCast(startPos, castRadius, chargeDir, out RaycastHit preHit, chargeDistance, groundLayer))
        {
            targetPos = preHit.point - (chargeDir * 2.0f);
        }

        targetPos.y = transform.position.y;

        bool hasHitPlayer = false;
        float checkDistance = 3.0f;

        Vector3 flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(targetPos.x, 0f, targetPos.z);

        if (anim != null) anim.SetTrigger(hashChargeAndCrash);
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.ChargeAttack);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.ChargeAttack);

        while (Vector3.Distance(flatCurrent, flatTarget) > 0.5f)
        {
            Vector3 currentRayStart = transform.position + transform.TransformDirection(sphereCastOffset);

            if (Physics.SphereCast(currentRayStart, castRadius, chargeDir, out RaycastHit wallHit, checkDistance, groundLayer))
            {
                Debug.Log($"[Boss2Controller] 벽({wallHit.collider.name}) 충돌 감지! 돌진 중단.");
                break;
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPos, chargeSpeed * Time.deltaTime);
            flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);

            CheckAndBlastProps(transform.position + transform.TransformDirection(chargeHitOffset), chargeHitRadius, 120f);

            if (!hasHitPlayer)
            {
                Vector3 hitSphereCenter = transform.position + transform.TransformDirection(chargeHitOffset);
                Collider[] hits = Physics.OverlapSphere(hitSphereCenter, chargeHitRadius, CombinedTargetLayer);

                foreach (Collider col in hits)
                {
                    if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
                    {
                        hasHitPlayer = true;

                        if (anim != null) anim.SetTrigger(hashChargeAndBoom);

                        KartInputManager kartInput = col.GetComponentInParent<KartInputManager>() ?? col.GetComponent<KartInputManager>();
                        if (kartInput != null) kartInput.enabled = false;

                        Rigidbody kartRb = col.GetComponentInParent<Rigidbody>() ?? col.GetComponent<Rigidbody>();
                        if (kartRb != null)
                        {
                            kartRb.linearVelocity = Vector3.zero;
                            kartRb.angularVelocity = Vector3.zero;
                        }

                        if (chargeMTEProcess != null)
                        {
                            KartEjectionSystem ejectionSys = col.GetComponentInParent<KartEjectionSystem>() ?? col.GetComponent<KartEjectionSystem>();

                            chargeMTEProcess.Reset();
                            chargeMTEProcess.Execute();

                            yield return StartCoroutine(MonitorMTEResultRoutine(chargeMTEProcess, kartInput, ejectionSys));
                        }
                        else
                        {
                            if (kartRb != null)
                            {
                                TryApplyKnockback(col, chargeHitForce, applyUpwardForce: true, applyDamage: true);
                            }
                        }
                        break;
                    }
                }
            }

            if (hasHitPlayer) break;

            yield return null;
        }

        yield return new WaitForSeconds(0.3f);

        Vector3 targetAscentPos = new Vector3(transform.position.x, originalPosition.y, transform.position.z);
        yield return StartCoroutine(MoveToPositionRoutine(targetAscentPos));

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private IEnumerator MonitorMTEResultRoutine(_Project.Scripts.VisualScripting.MTEProcess mte, KartInputManager kartInput, KartEjectionSystem ejectionSystem)
    {
        var waitingField = typeof(_Project.Scripts.VisualScripting.MTEProcess).GetField("_isWaitingInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        while (waitingField != null && (bool)waitingField.GetValue(mte) == true)
        {
            yield return null;
        }

        if (mte.IsOn)
        {
            Debug.Log("<color=green>[Boss2Controller] MTE 성공! 플레이어 카트 입력 복구 및 보스 스턴.</color>");

            if (kartInput != null) kartInput.enabled = true;
            InterruptAndStun();
        }
        else
        {
            Debug.Log("<color=red>[Boss2Controller] MTE 실패! 플레이어 사출 처리.</color>");

            if (ejectionSystem != null)
            {
                Transform jumpTarget = GetTargetForPhase(currentPhase);
                ejectionSystem.EjectPlayerToTarget(jumpTarget, resetJumpHeight, resetJumpDuration);
            }

            ChangeState(BossState.Idle);
        }
    }

    private Transform GetPlayerTransform()
    {
        if (playerObj != null) return playerObj.transform;

        if (rigController != null)
        {
            var pTrans = rigController.GetType().GetField("playerTransform", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rigController) as Transform;
            if (pTrans != null) return pTrans;
        }

        return null;
    }

    public IEnumerator ExecuteThrowPropSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteThrowPropSkill));

        List<GameObject> allProps = new List<GameObject>();
        if (lightPropPrefabs != null) allProps.AddRange(lightPropPrefabs);
        if (heavyPropPrefabs != null) allProps.AddRange(heavyPropPrefabs);

        if (allProps.Count > 0)
        {
            if (anim != null) anim.SetTrigger(hashThrowProp);

            Transform holdPoint = (lightPropHoldPoint != null) ? lightPropHoldPoint : transform;
            GameObject selectedPrefab = allProps[Random.Range(0, allProps.Count)];

            // 홀드 포인트의 자식 오브젝트로 생성 및 위치 초기화
            GameObject spawnedProp = Instantiate(selectedPrefab, holdPoint);
            spawnedProp.transform.localPosition = Vector3.zero;
            spawnedProp.transform.localRotation = Quaternion.identity;

            Rigidbody propRb = spawnedProp.GetComponent<Rigidbody>();
            if (propRb != null) propRb.isKinematic = true;

            PropController propCtrl = spawnedProp.GetComponent<PropController>();
            if (propCtrl == null) propCtrl = spawnedProp.AddComponent<PropController>();

            yield return new WaitForSeconds(pullAnimDuration);

            Transform pTrans = GetPlayerTransform();
            Vector3 targetPos = (pTrans != null) ? pTrans.position : (transform.position + transform.forward * 20f);
            Vector3 throwDir = (targetPos - spawnedProp.transform.position).normalized;

            SkillWarningIndicator.SpawnBox(spawnedProp.transform.position, throwDir, 6.0f, Vector3.Distance(spawnedProp.transform.position, targetPos) + 10f, throwReleaseDelay, new Color(1f, 0.5f, 0f, 0.35f), groundLayer);

            yield return new WaitForSeconds(throwReleaseDelay);

            if (spawnedProp != null && propCtrl != null)
            {
                // 발사 순간 부모 관계 해제 및 물리 활성화
                spawnedProp.transform.SetParent(null);

                if (propRb != null) propRb.isKinematic = false;

                if (throwDir.sqrMagnitude < 0.001f) throwDir = transform.forward;
                if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.ThrowProp);
                if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.ThrowProp);

                // [해결책 선택 적용] 목표 지점으로 투척
                propCtrl.ThrowAtTarget(targetPos, throwPropSpeed);
            }
        }

        yield return new WaitForSeconds(0.8f);
        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    public IEnumerator ExecuteThrowHeavyPropSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed("ChargeAndCrash");

        if (heavyPropPrefabs != null && heavyPropPrefabs.Count > 0)
        {
            if (anim != null) anim.SetTrigger(hashChargeAndCrash);

            Transform holdPoint = (heavyPropHoldPoint != null) ? heavyPropHoldPoint : transform;
            GameObject selectedPrefab = heavyPropPrefabs[Random.Range(0, heavyPropPrefabs.Count)];

            // 홀드 포인트의 자식 오브젝트로 생성 및 위치 초기화
            GameObject spawnedProp = Instantiate(selectedPrefab, holdPoint);
            spawnedProp.transform.localPosition = Vector3.zero;
            spawnedProp.transform.localRotation = Quaternion.identity;

            Rigidbody propRb = spawnedProp.GetComponent<Rigidbody>();
            if (propRb != null) propRb.isKinematic = true;

            PropController propCtrl = spawnedProp.GetComponent<PropController>();
            if (propCtrl == null) propCtrl = spawnedProp.AddComponent<PropController>();

            yield return new WaitForSeconds(pullAnimDuration);

            Transform pTrans = GetPlayerTransform();
            Vector3 targetPos = (pTrans != null) ? pTrans.position : (transform.position + transform.forward * 20f);
            Vector3 throwDir = (targetPos - spawnedProp.transform.position).normalized;

            SkillWarningIndicator.SpawnBox(spawnedProp.transform.position, throwDir, 8.0f, Vector3.Distance(spawnedProp.transform.position, targetPos) + 10f, throwReleaseDelay, new Color(1f, 0.2f, 0f, 0.4f), groundLayer);

            yield return new WaitForSeconds(throwReleaseDelay);

            if (spawnedProp != null && propCtrl != null)
            {
                // 발사 순간 부모 관계 해제 및 물리 활성화
                spawnedProp.transform.SetParent(null);

                if (propRb != null) propRb.isKinematic = false;

                if (throwDir.sqrMagnitude < 0.001f) throwDir = transform.forward;
                if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.ThrowHeavyProp);
                if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.ThrowHeavyProp);

                // [해결책 선택 적용] 목표 지점으로 투척
                propCtrl.ThrowAtTarget(targetPos, heavyThrowPropSpeed);
            }
        }

        yield return new WaitForSeconds(1.0f);
        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    public IEnumerator ExecuteFanSpinSkill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteFanSpinSkill));

        Vector3 pivotPos = SkillPivotPosition;

        SkillWarningIndicator.SpawnCircle(pivotPos, skillRanges.innerRadius, 1.2f + fanDuration, new Color(1f, 0.9f, 0f, 0.35f), groundLayer);

        if (rigController != null)
        {
            rigController.SetRigWeight(true);
        }

        if (anim != null) anim.SetTrigger(hashFanSpin);

        yield return new WaitForSeconds(1.2f);

        GameObject fanVfxInstance = null;
        if (skillEffectOutput != null)
        {
            fanVfxInstance = skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.FanSpin);
        }

        float elapsedTime = 0f;

        while (elapsedTime < fanDuration)
        {
            elapsedTime += Time.fixedDeltaTime;

            Collider[] hitColliders = Physics.OverlapSphere(pivotPos, skillRanges.innerRadius, CombinedTargetLayer);

            foreach (Collider col in hitColliders)
            {
                if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
                {
                    Rigidbody targetRb = col.GetComponentInParent<Rigidbody>() ?? col.GetComponent<Rigidbody>();

                    if (targetRb != null)
                    {
                        Vector3 pushDir = col.transform.position - transform.position;
                        pushDir.y = 0f;
                        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.FanSpin);

                        if (pushDir.sqrMagnitude < 0.001f) pushDir = transform.forward;
                        pushDir.Normalize();

                        Vector3 pushVel = pushDir * fanPushForce;
                        pushVel.y = targetRb.linearVelocity.y;
                        targetRb.linearVelocity = pushVel;
                    }
                }
            }

            yield return new WaitForFixedUpdate();
        }

        if (fanVfxInstance != null)
        {
            Destroy(fanVfxInstance);
        }

        yield return new WaitForSeconds(0.5f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    public IEnumerator ExecuteWooferRush()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteWooferRush));

        Transform playerTrans = PrepareSkillFacing();

        if (playerTrans != null && Vector3.Distance(SkillPivotPosition, playerTrans.position) <= skillRanges.midRadius)
        {
            Vector3 loweredPos = new Vector3(transform.position.x, originalPosition.y - lowerAmount, transform.position.z);
            yield return StartCoroutine(MoveToPositionRoutine(loweredPos));

            Vector3 rushTargetPos = playerTrans.position;
            rushTargetPos.y = loweredPos.y;

            Vector3 lookDir = rushTargetPos - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                transform.rotation = targetRot;

                var bossRootField = rigController?.GetType().GetField("bossRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rigController) as Transform;
                if (bossRootField != null)
                {
                    bossRootField.rotation = targetRot;
                }
            }

            float rushDist = Vector3.Distance(transform.position, rushTargetPos);
            SkillWarningIndicator.SpawnBox(transform.position, lookDir.normalized, 30.0f, rushDist, 1.0f, new Color(1f, 0.2f, 0.2f, 0.4f), groundLayer);

            yield return new WaitForSeconds(1.0f);

            if (anim != null) anim.SetTrigger(hashWooperRush);
            if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.WooferRush);
            if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.WooferRush);

            yield return new WaitForSeconds(0.3f);

            Vector3 flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 flatTarget = new Vector3(rushTargetPos.x, 0f, rushTargetPos.z);
            bool hasHitPlayer = false;

            while (Vector3.Distance(flatCurrent, flatTarget) > 0.5f)
            {
                transform.position = Vector3.MoveTowards(transform.position, rushTargetPos, wooferRushSpeed * Time.deltaTime);
                flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);

                CheckAndBlastProps(transform.position, 15.0f, wooferRushHitForce);

                if (!hasHitPlayer)
                {
                    Collider[] hits = Physics.OverlapSphere(transform.position, 15.0f, CombinedTargetLayer);
                    foreach (Collider col in hits)
                    {
                        if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper") || col.CompareTag("Player"))
                        {
                            hasHitPlayer = true;
                            TryApplyKnockback(col, wooferRushHitForce, applyUpwardForce: true, applyDamage: true);

                            if (wooferRushHitOutputs != null && wooferRushHitOutputs.Count > 0)
                            {
                                foreach (var output in wooferRushHitOutputs)
                                {
                                    if (output != null)
                                    {
                                        if (output is _Project.Scripts.VisualScripting.ComicHitPopupBossOutput comicPopup)
                                        {
                                            comicPopup.SetTargetTransform(col.transform);
                                        }

                                        output.Execute();
                                    }
                                }
                            }

                            break;
                        }
                    }
                }

                if (hasHitPlayer) break;

                yield return null;
            }

            yield return new WaitForSeconds(0.3f);

            Vector3 targetAscentPos = new Vector3(transform.position.x, originalPosition.y, transform.position.z);
            yield return StartCoroutine(MoveToPositionRoutine(targetAscentPos));
        }

        yield return new WaitForSeconds(0.2f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private IEnumerator DisablePlayerInputRoutine(KartInputManager kartInput, float duration)
    {
        if (kartInput == null) yield break;

        kartInput.enabled = false;
        yield return new WaitForSeconds(duration);
        kartInput.enabled = true;
    }

    public IEnumerator ExecuteWoofer1Skill()
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(nameof(ExecuteWoofer1Skill));

        Vector3 spawnPos = skillEffectOutput != null
            ? skillEffectOutput.GetSpawnPosition(_Project.Scripts.VisualScripting.BossSkillType.Woofer1)
            : transform.position;

        SkillWarningIndicator.SpawnCircle(spawnPos, skillRanges.outerRadius, 0.4f, new Color(1f, 0.3f, 0.3f, 0.35f), groundLayer);

        if (anim != null) anim.SetTrigger(hashWooperAttack);

        yield return new WaitForSeconds(0.4f);

        if (wooferFullscreenEffect != null)
        {
            wooferFullscreenEffect.TriggerShockwave(spawnPos, skillRanges.outerRadius, 0.6f);
        }

        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.Woofer1);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.Woofer1);

        ApplyWooferKnockback(skillRanges.outerRadius, true);

        Collider[] hits = Physics.OverlapSphere(spawnPos, skillRanges.outerRadius, CombinedTargetLayer);
        foreach (Collider col in hits)
        {
            if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
            {
                KartInputManager kartInput = col.GetComponentInParent<KartInputManager>() ?? col.GetComponent<KartInputManager>();
                if (kartInput != null)
                {
                    StartCoroutine(DisablePlayerInputRoutine(kartInput, woofer1DisableInputDuration));
                }
                break;
            }
        }

        yield return new WaitForSeconds(0.8f);

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void CheckWaterZoneInPhase3()
    {
        if (currentPhase != BossPhase.Phase3 || currentState == BossState.Stunned || currentState == BossState.Defeated)
        {
            return;
        }

        Collider[] waterZoneHits = Physics.OverlapSphere(SkillPivotPosition, waterZoneCheckRadius);
        HashSet<Collider> detectedThisFrame = new HashSet<Collider>();

        foreach (Collider col in waterZoneHits)
        {
            if (col.CompareTag("WaterZone"))
            {
                detectedThisFrame.Add(col);

                if (!currentWaterZones.Contains(col))
                {
                    waterStack++;
                    Debug.Log($"<color=blue>[Boss2Controller] WaterZone 감지! 현재 스택: {waterStack}/3</color>");

                    if (waterStack >= 3)
                    {
                        Debug.Log("<color=red>[Boss2Controller] WaterZone 3스택 달성! 즉시 기절 상태로 전환합니다.</color>");
                        waterStack = 0;
                        currentWaterZones.Clear();

                        if (anim != null) anim.SetBool(hashMovePosition, false);

                        InterruptAndStun(isWaterStun: true);
                        return;
                    }
                }
            }
        }

        currentWaterZones = detectedThisFrame;
    }

    private void DetachBossParts(PhaseDetachedPropData detachData)
    {
        if (detachData.partsToDisable != null)
        {
            foreach (GameObject part in detachData.partsToDisable)
            {
                if (part != null) part.SetActive(false);
            }
        }

        if (detachData.heavyPropPrefabsToDrop == null || detachData.heavyPropPrefabsToDrop.Length == 0) return;

        for (int i = 0; i < detachData.heavyPropPrefabsToDrop.Length; i++)
        {
            GameObject prefab = detachData.heavyPropPrefabsToDrop[i];
            if (prefab == null) continue;

            Vector3 dropPos = transform.position;
            Quaternion dropRot = transform.rotation;

            if (detachData.partsToDisable != null && detachData.partsToDisable.Length > 0)
            {
                int targetIndex = Mathf.Clamp(i, 0, detachData.partsToDisable.Length - 1);
                if (detachData.partsToDisable[targetIndex] != null)
                {
                    dropPos = detachData.partsToDisable[targetIndex].transform.position;
                    dropRot = detachData.partsToDisable[targetIndex].transform.rotation;
                }
            }

            GameObject droppedProp = Instantiate(prefab, dropPos, dropRot);

            PropController propCtrl = droppedProp.GetComponent<PropController>();
            if (propCtrl != null)
            {
                propCtrl.weightType = PropWeight.Heavy;

                Rigidbody rb = droppedProp.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    Vector3 explodeDir = (dropPos - transform.position).normalized;
                    explodeDir.y = 0.5f;
                    rb.AddForce(explodeDir * detachDropForce, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * detachDropForce, ForceMode.Impulse);
                }
            }
        }
    }

    private void ApplyWooferKnockback(float maxRadius, bool useMultiRangeScaling)
    {
        Vector3 centerPos = SkillPivotPosition;
        Collider[] hitColliders = Physics.OverlapSphere(centerPos, maxRadius, CombinedTargetLayer);

        foreach (Collider col in hitColliders)
        {
            if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
            {
                float distance = Vector3.Distance(centerPos, col.transform.position);
                float appliedForce = innerKnockbackForce;

                if (useMultiRangeScaling)
                {
                    if (distance <= skillRanges.innerRadius) appliedForce = innerKnockbackForce;
                    else if (distance <= skillRanges.midRadius) appliedForce = midKnockbackForce;
                    else if (distance <= skillRanges.outerRadius) appliedForce = outerKnockbackForce;
                }

                TryApplyKnockback(col, appliedForce, applyUpwardForce: false, applyDamage: false);
            }
        }
    }

    public IEnumerator ExecuteSpawnPropsSkill()
    {
        ChangeState(BossState.UsingSkill);

        if (rigController != null)
        {
            Vector3 targetFacingDir = transform.forward;

            if (spawnSkillFacingTarget != null)
            {
                targetFacingDir = spawnSkillFacingTarget.position - transform.position;
                targetFacingDir.y = 0f;
            }

            if (targetFacingDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(targetFacingDir.normalized, Vector3.up);

                var bossRootField = rigController.GetType().GetField("bossRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(rigController) as Transform;
                if (bossRootField != null) bossRootField.rotation = targetRot;
            }
        }

        if (anim != null)
        {
            switch (currentPhase)
            {
                case BossPhase.Phase1:
                    anim.SetTrigger(hashDropProp);
                    break;
                case BossPhase.Phase2:
                case BossPhase.Phase2_5:
                    anim.SetTrigger(hashDropPropByLand);
                    break;
                case BossPhase.Phase3:
                    anim.SetTrigger(hashDropPropByElec);
                    break;
            }
        }

        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.SpawnProps);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.SpawnProps);

        yield return new WaitForSeconds(spawnAnimDelay);

        SpawnRandomPropsByPhase(currentPhase);

        yield return new WaitForSeconds(0.8f);
        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    #region Grid Prop Spawn System
    public void SpawnRandomPropsByPhase(BossPhase phase)
    {
        int phaseIndex = (int)phase;

        if (phase == BossPhase.Phase3)
        {
            phaseIndex--;
        }

        if (phaseSpawnDataArray == null || phaseIndex >= phaseSpawnDataArray.Length)
            return;

        PhaseSpawnData currentData = phaseSpawnDataArray[phaseIndex];
        GenerateGridAndSpawn(currentData, phase);
    }

    private void GenerateGridAndSpawn(PhaseSpawnData data, BossPhase phase)
    {
        ClearActiveSprinklers();

        Vector3 centerPos = data.spawnAreaCenter != null ? data.spawnAreaCenter.position : transform.position;
        float cellSize = Mathf.Max(1.0f, data.cellSize);

        int cols = Mathf.Max(1, Mathf.FloorToInt(data.spawnAreaSize.x / cellSize));
        int rows = Mathf.Max(1, Mathf.FloorToInt(data.spawnAreaSize.y / cellSize));

        List<Vector2Int> allGridCells = new List<Vector2Int>();
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                allGridCells.Add(new Vector2Int(c, r));
            }
        }

        HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

        bool canSpawnSprinkler = (phase == BossPhase.Phase2 || phase == BossPhase.Phase3) && sprinklerPrefab != null;
        int sprinklersToSpawn = canSpawnSprinkler ? data.sprinklerCount : 0;

        List<Vector2Int> sprinklerCandidates = new List<Vector2Int>(allGridCells);

        for (int i = 0; i < sprinklersToSpawn; i++)
        {
            if (sprinklerCandidates.Count == 0)
            {
                Debug.LogWarning("[Boss2Controller] 공간이 부족하여 설정된 수만큼의 스프링클러를 전부 배치하지 못했습니다.");
                break;
            }

            int randomIndex = Random.Range(0, sprinklerCandidates.Count);
            Vector2Int chosenCell = sprinklerCandidates[randomIndex];

            Vector3 spawnPos = GridToWorldPosition(chosenCell, cols, rows, centerPos, data.spawnAreaSize, cellSize);
            GameObject spawnedSprinkler = Instantiate(sprinklerPrefab, spawnPos, Quaternion.identity);
            activeSprinklers.Add(spawnedSprinkler);

            occupiedCells.Add(chosenCell);

            for (int dc = -1; dc <= 1; dc++)
            {
                for (int dr = -1; dr <= 1; dr++)
                {
                    Vector2Int neighborCell = new Vector2Int(chosenCell.x + dc, chosenCell.y + dr);
                    sprinklerCandidates.Remove(neighborCell);
                }
            }
        }

        List<Vector2Int> propCandidates = new List<Vector2Int>();
        foreach (var cell in allGridCells)
        {
            if (!occupiedCells.Contains(cell))
            {
                propCandidates.Add(cell);
            }
        }

        ShuffleList(propCandidates);

        int lightSpawned = 0;
        while (lightSpawned < data.lightPropCount && propCandidates.Count > 0 && lightPropPrefabs != null && lightPropPrefabs.Count > 0)
        {
            Vector2Int cell = propCandidates[0];
            propCandidates.RemoveAt(0);

            GameObject randomPrefab = lightPropPrefabs[Random.Range(0, lightPropPrefabs.Count)];
            Vector3 groundPos = GridToWorldPosition(cell, cols, rows, centerPos, data.spawnAreaSize, cellSize);
            Vector3 dropPos = groundPos + Vector3.up * spawnHeightOffset;

            GameObject spawnedProp = Instantiate(randomPrefab, dropPos, Quaternion.Euler(Random.insideUnitSphere * 360f));
            PropController propCtrl = spawnedProp.GetComponent<PropController>();
            if (propCtrl != null)
            {
                propCtrl.SetStartingPosition(groundPos);
            }

            lightSpawned++;
        }

        int heavySpawned = 0;
        while (heavySpawned < data.heavyPropCount && propCandidates.Count > 0 && heavyPropPrefabs != null && heavyPropPrefabs.Count > 0)
        {
            Vector2Int cell = propCandidates[0];
            propCandidates.RemoveAt(0);

            GameObject randomPrefab = heavyPropPrefabs[Random.Range(0, heavyPropPrefabs.Count)];
            Vector3 groundPos = GridToWorldPosition(cell, cols, rows, centerPos, data.spawnAreaSize, cellSize);
            Vector3 dropPos = groundPos + Vector3.up * spawnHeightOffset;

            GameObject spawnedProp = Instantiate(randomPrefab, dropPos, Quaternion.Euler(Random.insideUnitSphere * 360f));
            PropController propCtrl = spawnedProp.GetComponent<PropController>();
            if (propCtrl != null)
            {
                propCtrl.SetStartingPosition(groundPos);
            }

            heavySpawned++;
        }
    }

    private Vector3 GridToWorldPosition(Vector2Int cell, int cols, int rows, Vector3 center, Vector2 areaSize, float cellSize)
    {
        float startX = center.x - (areaSize.x * 0.5f);
        float startZ = center.z - (areaSize.y * 0.5f);

        float posX = startX + (cell.x + 0.5f) * cellSize;
        float posZ = startZ + (cell.y + 0.5f) * cellSize;

        return new Vector3(posX, center.y, posZ);
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[rnd];
            list[rnd] = temp;
        }
    }

    private void ClearActiveSprinklers()
    {
        for (int i = 0; i < activeSprinklers.Count; i++)
        {
            if (activeSprinklers[i] != null) Destroy(activeSprinklers[i]);
        }
        activeSprinklers.Clear();
    }
    #endregion

    public IEnumerator ExecuteChargeAttackSkillCustom(bool isBoomType)
    {
        ChangeState(BossState.UsingSkill);
        MarkSkillUsed(isBoomType ? "ChargeAndBoom" : "ChargeAndCrash");

        Vector3 loweredPos = new Vector3(transform.position.x, originalPosition.y - lowerAmount, transform.position.z);
        yield return StartCoroutine(MoveToPositionRoutine(loweredPos));

        Transform actualModelTransform = visualRoot != null ? visualRoot : transform;
        Vector3 chargeDir = actualModelTransform.forward;
        chargeDir.y = 0f;
        chargeDir.Normalize();

        SkillWarningIndicator.SpawnBox(transform.position, chargeDir, castRadius * 2f, chargeDistance, chargePrepTime, new Color(1f, 0.5f, 0f, 0.4f), groundLayer);

        yield return new WaitForSeconds(chargePrepTime);

        Vector3 localToWorldOffset = transform.TransformDirection(sphereCastOffset);
        Vector3 startPos = transform.position + localToWorldOffset;
        Vector3 targetPos = transform.position + chargeDir * chargeDistance;

        if (Physics.SphereCast(startPos, castRadius, chargeDir, out RaycastHit preHit, chargeDistance, groundLayer))
        {
            targetPos = preHit.point - (chargeDir * 2.0f);
        }
        targetPos.y = transform.position.y;

        bool hasHitPlayer = false;
        Vector3 flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(targetPos.x, 0f, targetPos.z);

        if (anim != null)
        {
            anim.SetTrigger(isBoomType ? hashChargeAndBoom : hashChargeAndCrash);
        }
        if (skillEffectOutput != null) skillEffectOutput.PlayEffect(_Project.Scripts.VisualScripting.BossSkillType.ChargeAttack);
        if (cameraShaker != null) cameraShaker.Shake(_Project.Scripts.VisualScripting.BossSkillType.ChargeAttack);

        while (Vector3.Distance(flatCurrent, flatTarget) > 0.5f)
        {
            Vector3 currentRayStart = transform.position + transform.TransformDirection(sphereCastOffset);
            if (Physics.SphereCast(currentRayStart, castRadius, chargeDir, out RaycastHit wallHit, 3.0f, groundLayer))
            {
                break;
            }

            transform.position = Vector3.MoveTowards(transform.position, targetPos, chargeSpeed * Time.deltaTime);
            flatCurrent = new Vector3(transform.position.x, 0f, transform.position.z);

            CheckAndBlastProps(transform.position + transform.TransformDirection(chargeHitOffset), chargeHitRadius, 120f);

            if (!hasHitPlayer)
            {
                Collider[] hits = Physics.OverlapSphere(transform.position + transform.TransformDirection(chargeHitOffset), chargeHitRadius, CombinedTargetLayer);
                foreach (Collider col in hits)
                {
                    if (col.CompareTag("PlayerBumper") || col.CompareTag("Bumper"))
                    {
                        hasHitPlayer = true;
                        TryApplyKnockback(col, chargeHitForce, applyUpwardForce: true, applyDamage: true);
                        break;
                    }
                }
            }

            if (hasHitPlayer) break;
            yield return null;
        }

        yield return new WaitForSeconds(0.3f);

        Vector3 targetAscentPos = new Vector3(transform.position.x, originalPosition.y, transform.position.z);
        yield return StartCoroutine(MoveToPositionRoutine(targetAscentPos));

        if (currentState != BossState.Stunned && currentState != BossState.Defeated)
        {
            ChangeState(BossState.Idle);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("PlayerBumper") || collision.gameObject.CompareTag("Bumper") || collision.gameObject.CompareTag("Player"))
        {
            if (currentPhase == BossPhase.Phase3 && currentState == BossState.Stunned && isWaterStunned)
            {
                Debug.Log("<color=cyan>[Boss2Controller] 3페이즈 WaterZone 기절 중 플레이어 충돌! 타겟으로 점프 사출합니다.</color>");

                KartEjectionSystem ejectionSystem = collision.gameObject.GetComponentInParent<KartEjectionSystem>() ?? collision.gameObject.GetComponent<KartEjectionSystem>();

                if (ejectionSystem != null)
                {
                    Transform phase3Target = GetTargetForPhase(BossPhase.Phase3);
                    ejectionSystem.EjectPlayerToTarget(phase3Target, resetJumpHeight, resetJumpDuration);
                }
                return;
            }

            if (isVulnerable)
            {
                _hasPlayerHitDuringCharge = true;

                if (currentPhase == BossPhase.Phase2_5)
                {
                    Debug.Log("<color=cyan>[Boss2Controller] 2.5페이즈 충전 중 충돌! 다음 씬으로 전환합니다.</color>");

                    InterruptAndStun();

                    if (!string.IsNullOrEmpty(nextSceneName))
                    {
                        SceneManager.LoadScene(nextSceneName);
                    }
                    else
                    {
                        int nextBuildIndex = SceneManager.GetActiveScene().buildIndex + 1;
                        SceneManager.LoadScene(nextBuildIndex);
                    }
                    return;
                }

                InterruptAndStun();

                KartEjectionSystem ejectionSystem = collision.gameObject.GetComponentInParent<KartEjectionSystem>() ?? collision.gameObject.GetComponent<KartEjectionSystem>();

                if (ejectionSystem != null)
                {
                    Transform phaseTarget = GetTargetForPhase(currentPhase);
                    ejectionSystem.EjectPlayerToTarget(phaseTarget, resetJumpHeight, resetJumpDuration);
                }
                else
                {
                    Debug.LogWarning("[Boss2Controller] 충돌한 범퍼카에서 KartEjectionSystem을 찾을 수 없습니다!");
                }
            }
        }
    }

    public void InterruptAndStun(bool isWaterStun = false)
    {
        StopAllCoroutines();

        if (punchRig != null)
        {
            punchRig.weight = 0f;
        }

        waterStack = 0;
        currentWaterZones.Clear();
        isWaterStunned = isWaterStun;

        SetChargingState(false);
        if (anim != null)
        {
            anim.SetBool(hashMinigun, false);
            anim.SetBool(hashMovePosition, false);
            anim.SetBool(hashCharge, false);
            anim.SetBool(hashDuringCharge, false);
        }

        ChangeState(BossState.Stunned);

        if (isWaterStun)
        {
            Vector3 targetLoweredPos = new Vector3(transform.position.x, originalPosition.y - lowerAmount, transform.position.z);
            StartMoveToPosition(targetLoweredPos);
        }

        PropController[] activeProps = FindObjectsByType<PropController>(FindObjectsSortMode.None);
        foreach (PropController prop in activeProps)
        {
            if (prop != null) prop.Drop();
        }

        aiCoroutine = StartCoroutine(AILoopRoutine());

        if (currentPhase != BossPhase.Phase3)
        {
            recoveryCoroutine = StartCoroutine(StunRecoveryRoutine());
        }
        else
        {
            Debug.Log("<color=magenta>[Boss2Controller] 3페이즈 스턴 상태 진입. 복구 타이머가 작동하지 않습니다.</color>");
        }
    }

    private IEnumerator StunRecoveryRoutine()
    {
        if (currentPhase == BossPhase.Phase3)
        {
            recoveryCoroutine = null;
            yield break;
        }

        yield return new WaitForSeconds(stunDuration);

        recoveryCoroutine = null;
        OnRecoverFromStun(currentPhase, isPhaseAdvance: false);

        yield return new WaitForSeconds(2.0f);
    }

    public void ForceRecoverFromStun(BossPhase targetPhase)
    {
        if (recoveryCoroutine != null)
        {
            StopCoroutine(recoveryCoroutine);
            recoveryCoroutine = null;
        }

        OnRecoverFromStun(targetPhase, isPhaseAdvance: true);
    }

    public Transform GetTargetForPhase(BossPhase phase)
    {
        switch (phase)
        {
            case BossPhase.Phase1: return phase1JumpTarget;
            case BossPhase.Phase3: return phase3JumpTarget;
            default: return phase1JumpTarget;
        }
    }

    private void OnRecoverFromStun(BossPhase nextPhase, bool isPhaseAdvance)
    {
        waterStack = 0;
        currentWaterZones.Clear();

        if (isWaterStunned)
        {
            Vector3 restoredPos = new Vector3(transform.position.x, originalPosition.y, transform.position.z);
            StartMoveToPosition(restoredPos);
        }

        isWaterStunned = false;

        if (currentPhase == BossPhase.Phase1 && nextPhase == BossPhase.Phase2)
        {
            StartCoroutine(Phase2TransitionRoutine(nextPhase, isPhaseAdvance));
            return;
        }

        if ((currentPhase == BossPhase.Phase2 || currentPhase == BossPhase.Phase2_5) && nextPhase == BossPhase.Phase3)
        {
            DetachBossParts(phase2DetachData);
        }

        currentPhase = nextPhase;
        phaseSpawnExecuted = false;
        usedSkillsInCurrentPhase.Clear();

        if (nextPhase == BossPhase.Phase3)
        {
            ResetSkillAnimatorTriggers();
        }

        ClearActiveSprinklers();

        Transform playerTrans = GetPlayerTransform();
        if (playerTrans != null) SetRigTarget(playerTrans);

        ChangeState(BossState.Idle);
        ClearAllPropsInField();

        if (isPhaseAdvance && nextPhase != BossPhase.Phase3)
        {
            StartCoroutine(RecoverSequenceRoutine());
        }
    }

    private IEnumerator Phase2TransitionRoutine(BossPhase nextPhase, bool isPhaseAdvance)
    {
        ChangeState(BossState.Stunned);
        DetachBossParts(phase1DetachData);

        yield return new WaitForSeconds(phase2TransitionStunDelay);

        currentPhase = nextPhase;
        phaseSpawnExecuted = false;
        usedSkillsInCurrentPhase.Clear();

        if (currentBattery < 80f)
        {
            currentBattery = 80f;
        }

        ClearActiveSprinklers();
        ClearAllPropsInField();

        if (isPhaseAdvance)
        {
            StartCoroutine(RecoverSequenceRoutine());
        }

        if (phase2StartPoint != null)
        {
            ChangeState(BossState.UsingSkill);
            yield return StartCoroutine(MoveToPositionRoutine(phase2StartPoint.position));
        }

        ChangeState(BossState.Idle);
    }

    private IEnumerator RecoverSequenceRoutine()
    {
        yield return new WaitForSeconds(2.0f);

        Rigidbody kartRb = (bumperObj != null) ? bumperObj.GetComponentInParent<Rigidbody>() ?? bumperObj.GetComponent<Rigidbody>() : null;
        Rigidbody playerRb = (playerObj != null) ? playerObj.GetComponentInParent<Rigidbody>() ?? playerObj.GetComponent<Rigidbody>() : null;

        PlayerLocomotion playerLoco = (playerObj != null) ? playerObj.GetComponentInParent<PlayerLocomotion>() ?? playerObj.GetComponent<PlayerLocomotion>() : null;

        if (kartRb != null)
        {
            kartRb.linearVelocity = Vector3.zero;
            kartRb.angularVelocity = Vector3.zero;
            kartRb.isKinematic = true;
        }

        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
            playerRb.isKinematic = true;
        }

        Vector3 kartStartPos = (bumperObj != null) ? bumperObj.transform.position : Vector3.zero;
        Quaternion kartStartRot = (bumperObj != null) ? bumperObj.transform.rotation : Quaternion.identity;

        Vector3 playerStartPos = (playerObj != null) ? playerObj.transform.position : Vector3.zero;
        Quaternion playerStartRot = (playerObj != null) ? playerObj.transform.rotation : Quaternion.identity;

        Vector3 targetKartPos = (kartResetPoint != null) ? kartResetPoint.position : kartStartPos;
        Vector3 targetPlayerPos = (playerResetPoint != null) ? playerResetPoint.position : playerStartPos;

        float timer = 0f;
        while (timer < resetPushDuration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / resetPushDuration);
            float smoothT = 1f - (1f - t) * (1f - t);

            if (bumperObj != null && kartResetPoint != null)
            {
                bumperObj.transform.position = Vector3.Lerp(kartStartPos, targetKartPos, smoothT);
                bumperObj.transform.rotation = Quaternion.Slerp(kartStartRot, kartResetPoint.rotation, smoothT);
            }

            if (playerObj != null && playerResetPoint != null)
            {
                playerObj.transform.position = Vector3.Lerp(playerStartPos, targetPlayerPos, smoothT);
                playerObj.transform.rotation = Quaternion.Slerp(playerStartRot, playerResetPoint.rotation, smoothT);
            }

            yield return null;
        }

        if (bumperObj != null && kartResetPoint != null)
        {
            bumperObj.transform.position = targetKartPos;
            bumperObj.transform.rotation = kartResetPoint.rotation;
        }

        if (playerObj != null && playerResetPoint != null)
        {
            playerObj.transform.position = targetPlayerPos;
            playerObj.transform.rotation = playerResetPoint.rotation;
        }

        if (playerLoco != null)
        {
            playerLoco.SetAbsolutePaperState(true);
        }

        if (kartRb != null)
        {
            kartRb.linearVelocity = Vector3.zero;
            kartRb.angularVelocity = Vector3.zero;
            kartRb.isKinematic = false;
        }
        if (playerRb != null)
        {
            playerRb.linearVelocity = Vector3.zero;
            playerRb.angularVelocity = Vector3.zero;
            playerRb.isKinematic = false;
        }
    }
    

    private void ClearAllPropsInField()
    {
        PropController[] allProps = FindObjectsByType<PropController>(FindObjectsSortMode.None);
        foreach (PropController prop in allProps)
        {
            if (prop != null)
            {
                prop.BlastAway(transform.position, propBlastForce);
            }
        }
    }

    private void TryApplyKnockback(Collider col, float force, bool applyUpwardForce = true, bool applyDamage = false)
    {
        if (col == null) return;

        bool isPlayerTarget = col.CompareTag("PlayerBumper") || col.CompareTag("Bumper") || col.CompareTag("Player");

        if (col.CompareTag("Prop") || isPlayerTarget)
        {
            if (applyDamage && isPlayerTarget)
            {
                KartEnvironmentalHazard hazard = col.GetComponentInParent<KartEnvironmentalHazard>()
                    ?? col.GetComponent<KartEnvironmentalHazard>()
                    ?? col.GetComponentInChildren<KartEnvironmentalHazard>();

                if (hazard != null)
                {
                    hazard.TakeDamage(1);
                }
            }

            Rigidbody rb = col.GetComponentInParent<Rigidbody>() ?? col.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 pushDir = (col.transform.position - transform.position);
                pushDir.y = 0f;

                if (pushDir.sqrMagnitude < 0.01f)
                {
                    pushDir = transform.forward;
                }
                else
                {
                    pushDir.Normalize();
                }

                if (isPlayerTarget)
                {
                    float currentDist = Vector3.Distance(transform.position, col.transform.position);
                    if (currentDist < 6.0f)
                    {
                        Vector3 safePos = transform.position + pushDir * 7.5f;
                        safePos.y = col.transform.position.y;
                        col.transform.position = safePos;
                    }
                }

                if (applyUpwardForce)
                {
                    pushDir.y = 0.35f;
                    pushDir.Normalize();
                }

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(pushDir * force, ForceMode.Impulse);
            }
        }
    }

    private void StartMoveToPosition(Vector3 targetPos)
    {
        if (movePositionCoroutine != null)
        {
            StopCoroutine(movePositionCoroutine);
        }
        movePositionCoroutine = StartCoroutine(MoveToPositionRoutine(targetPos));
    }

    private IEnumerator MoveToPositionRoutine(Vector3 targetPos)
    {
        while (Vector3.Distance(transform.position, targetPos) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPos;
    }

    private void OnDrawGizmosSelected()
    {
        if (phaseSpawnDataArray != null)
        {
            foreach (var data in phaseSpawnDataArray)
            {
                Vector3 center = data.spawnAreaCenter != null ? data.spawnAreaCenter.position : transform.position;

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(center, new Vector3(data.spawnAreaSize.x, 1f, data.spawnAreaSize.y));

                if (data.cellSize > 0.5f)
                {
                    int cols = Mathf.Max(1, Mathf.FloorToInt(data.spawnAreaSize.x / data.cellSize));
                    int rows = Mathf.Max(1, Mathf.FloorToInt(data.spawnAreaSize.y / data.cellSize));

                    Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
                    for (int c = 0; c < cols; c++)
                    {
                        for (int r = 0; r < rows; r++)
                        {
                            Vector3 cellPos = GridToWorldPosition(new Vector2Int(c, r), cols, rows, center, data.spawnAreaSize, data.cellSize);
                            Gizmos.DrawWireCube(cellPos, new Vector3(data.cellSize, 0.1f, data.cellSize));
                        }
                    }
                }
            }
        }

        Vector3 pivotPos = SkillPivotPosition;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pivotPos, skillRanges.skillRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(pivotPos, skillRanges.innerRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pivotPos, skillRanges.midRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(pivotPos, skillRanges.outerRadius);

        if (phase1JumpTarget != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(phase1JumpTarget.position, 1.2f);
        }

        if (phase3JumpTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(phase3JumpTarget.position, 1.2f);
        }

        if (phase2StartPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(phase2StartPoint.position, 2.0f);
        }

        if (kartResetPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(kartResetPoint.position, new Vector3(2f, 1f, 2f));
        }

        if (playerResetPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(playerResetPoint.position, 0.8f);
        }

        Transform actualModelTransform = visualRoot;
        if (actualModelTransform == null && rigController != null)
        {
            actualModelTransform = rigController.GetType()
                .GetField("bossRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(rigController) as Transform;
        }
        if (actualModelTransform == null) actualModelTransform = transform;

        Vector3 rayDir = actualModelTransform.forward;
        rayDir.y = 0f;
        if (rayDir.sqrMagnitude > 0.001f) rayDir.Normalize();

        Vector3 localToWorldOffset = transform.TransformDirection(sphereCastOffset);
        Vector3 preStart = transform.position + localToWorldOffset;
        Vector3 preEnd = preStart + rayDir * chargeDistance;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(preStart, castRadius);
        Gizmos.DrawWireSphere(preEnd, castRadius);
        DrawSphereCastLines(preStart, preEnd, castRadius);

        float checkDistance = 3.0f;
        Vector3 runStart = transform.position + localToWorldOffset;
        Vector3 runEnd = runStart + rayDir * checkDistance;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(runStart, castRadius);
        Gizmos.DrawWireSphere(runEnd, castRadius);
        DrawSphereCastLines(runStart, runEnd, castRadius);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Vector3 hitCenter = transform.position + transform.TransformDirection(chargeHitOffset);
        Gizmos.DrawWireSphere(hitCenter, chargeHitRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, koongImpactRadius);
    }

    public float BatteryNormalized => Mathf.Clamp01(currentBattery / maxBattery);

    private void DrawSphereCastLines(Vector3 start, Vector3 end, float radius)
    {
        Vector3 dir = (end - start).normalized;
        if (dir.sqrMagnitude < 0.001f) return;

        Vector3 up = Vector3.up;
        Vector3 right = Vector3.Cross(dir, up).normalized;
        up = Vector3.Cross(right, dir).normalized;

        Gizmos.DrawLine(start + right * radius, end + right * radius);
        Gizmos.DrawLine(start - right * radius, end - right * radius);
        Gizmos.DrawLine(start + up * radius, end + up * radius);
        Gizmos.DrawLine(start - up * radius, end - up * radius);
    }
}