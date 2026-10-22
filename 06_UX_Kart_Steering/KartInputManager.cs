using UnityEngine;
using KartGame.KartSystems;

public class KartInputManager : MonoBehaviour, IInput
{
    public enum ControlMode { Keyboard, AI }

    [Header("Control Settings")]
    public ControlMode mode = ControlMode.Keyboard; // 변수 하나로 상태 제어

    [Header("AI (Waypoint) Settings")]
    public Transform[] waypoints;
    public float arrivalDistance = 5f;
    private int currentWaypointIndex = 0;

    public InputData GenerateInput()
    {
        // ★ 핵심 추가: 스크립트가 비활성화(enabled = false) 상태라면 빈 입력 반환 (인풋 차단)
        if (!enabled)
        {
            return new InputData
            {
                Accelerate = false,
                Brake = false,
                TurnInput = 0f
            };
        }

        // 모드에 따라 다른 입력 함수 실행
        if (mode == ControlMode.Keyboard)
        {
            return GetKeyboardInput();
        }
        else
        {
            return GetAIInput();
        }
    }

    // 1. 키보드 조작 로직
    private InputData GetKeyboardInput()
    {
        float verticalInput = Input.GetAxisRaw("Vertical");
        float horizontalInput = Input.GetAxis("Horizontal");

        return new InputData
        {
            Accelerate = verticalInput > 0,
            Brake = false, // 후진/브레이크 입력 제거
            TurnInput = horizontalInput // 감도 감쇠 없이 원본 회전 값 사용
        };
    }

    // 2. 웨이포인트 이동 로직
    private InputData GetAIInput()
    {
        if (waypoints == null || waypoints.Length == 0) return new InputData();

        Transform target = waypoints[currentWaypointIndex];
        Vector3 localTarget = transform.InverseTransformPoint(target.position);
        float distance = Vector3.Distance(transform.position, target.position);

        // 도착 체크
        if (distance < arrivalDistance)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }

        // 조향 계산 (Target이 내 왼쪽에 있으면 -1, 오른쪽에 있으면 1에 가깝게)
        float steer = Mathf.Clamp(localTarget.x / localTarget.magnitude, -1f, 1f);

        // 전방에 목표가 있을 때만 가속 (뒤에 있으면 회전부터 하도록)
        bool accel = localTarget.z > 0;

        return new InputData
        {
            Accelerate = accel,
            Brake = false,
            TurnInput = steer
        };
    }

    // 외부에서 모드를 바꿀 수 있는 함수 (예: 버튼 클릭 등)
    public void SetControlMode(bool isAI)
    {
        mode = isAI ? ControlMode.AI : ControlMode.Keyboard;
    }
}