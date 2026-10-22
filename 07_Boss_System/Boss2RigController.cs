using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class Boss2RigController : MonoBehaviour
{
    [Header("Targets & Transforms")]
    [SerializeField] private Transform playerTransform;   // 플레이어 Transform
    [SerializeField] private Transform bossRoot;          // 보스의 몸통/루트 Transform (Boss_Body_Rotator)
    [SerializeField] private Transform headTarget;        // LookAt Constraint가 바라볼 Boss_HeadTarget

    [Header("Rig Component Reference")]
    [SerializeField] private Rig headRig;                 // Head_Rig 오브젝트의 Rig 컴포넌트

    [Header("Tracking Settings")]
    [SerializeField] private float maxRotationAngle = 50f; // 몸통 회전을 시작할 한계 각도 (도)
    [SerializeField] private float bodyRotateSpeed = 120f;  // 몸통 회전 속도 (deg/sec)
    [SerializeField] private float headDampSpeed = 20f;    // HeadTarget 추적 속도
    [SerializeField] private float rigFadeSpeed = 2f;      // Rig Weight가 0<->1 변하는 속도

    private Quaternion targetBodyRotation;
    private bool isBodyRotating = false;
    private bool isTrackingAllowed = true;
    private Coroutine rigFadeCoroutine;

    private void Start()
    {
        if (bossRoot != null)
        {
            targetBodyRotation = bossRoot.rotation;
        }
    }

    private void Update()
    {
        if (playerTransform == null || headTarget == null || bossRoot == null) return;

        // 1. HeadTarget(시선) 위치 업데이트
        headTarget.position = Vector3.Lerp(
            headTarget.position,
            playerTransform.position,
            Time.deltaTime * headDampSpeed
        );

        // 스킬/스턴/패배 등으로 트래킹이 비활성화된 상태라면 몸통 회전 계산을 건너뜀
        if (!isTrackingAllowed) return;

        // 2. 몸통 관점의 진짜 플레이어 방향 계산 (Y축 평면화)
        Vector3 dirToRealPlayer = playerTransform.position - bossRoot.position;
        dirToRealPlayer.y = 0f;

        if (dirToRealPlayer.sqrMagnitude < 0.001f) return;

        // 3. 현재 몸통 정면과 플레이어 위치 간의 각도 계산
        float currentAngle = Vector3.SignedAngle(bossRoot.forward, dirToRealPlayer, Vector3.up);

        // 4. 회전 상태 제어 (히스테리시스 적용)
        if (Mathf.Abs(currentAngle) > maxRotationAngle)
        {
            isBodyRotating = true;
        }
        else if (Mathf.Abs(currentAngle) < 10f)
        {
            isBodyRotating = false;
        }

        // 5. 회전 중일 때는 플레이어 방향을 목표로 설정
        if (isBodyRotating)
        {
            targetBodyRotation = Quaternion.LookRotation(dirToRealPlayer, Vector3.up);
        }
    }

    private void LateUpdate()
    {
        if (bossRoot == null || !isTrackingAllowed) return;

        // 6. RotateTowards를 통해 몸통을 부드럽게 회전
        bossRoot.rotation = Quaternion.RotateTowards(bossRoot.rotation, targetBodyRotation, bodyRotateSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 상태 변화 시 외부(Boss2Controller)에서 호출하는 함수
    /// </summary>
    public void SetRigWeight(bool enableTracking)
    {
        isTrackingAllowed = enableTracking;

        if (rigFadeCoroutine != null)
        {
            StopCoroutine(rigFadeCoroutine);
        }

        float targetWeight = enableTracking ? 1f : 0f;
        rigFadeCoroutine = StartCoroutine(FadeRigWeightRoutine(targetWeight));
    }

    private IEnumerator FadeRigWeightRoutine(float targetWeight)
    {
        if (headRig == null) yield break;

        while (!Mathf.Approximately(headRig.weight, targetWeight))
        {
            headRig.weight = Mathf.MoveTowards(headRig.weight, targetWeight, Time.deltaTime * rigFadeSpeed);
            yield return null;
        }

        headRig.weight = targetWeight;
    }

    private void OnDrawGizmosSelected()
    {
        if (bossRoot == null) return;

        Gizmos.color = Color.yellow;
        Vector3 leftBound = Quaternion.Euler(0, -maxRotationAngle, 0) * bossRoot.forward;
        Vector3 rightBound = Quaternion.Euler(0, maxRotationAngle, 0) * bossRoot.forward;

        Gizmos.DrawRay(bossRoot.position, leftBound * 5f);
        Gizmos.DrawRay(bossRoot.position, rightBound * 5f);
    }
}