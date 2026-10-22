using UnityEngine;

public class SkillWarningIndicator : MonoBehaviour
{
    private MeshRenderer meshRenderer;

    // 원형 장판 생성 (groundLayer 수신으로 바닥 위치 자동 탐색)
    public static SkillWarningIndicator SpawnCircle(Vector3 center, float radius, float duration, Color color, LayerMask groundLayer = default)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>()); // 콜라이더 제거

        // 레이캐스트로 실제 바닥 지면 좌표 탐색
        Vector3 groundPos = GetGroundPosition(center, groundLayer);
        go.transform.position = groundPos + Vector3.up * 0.05f; // 지면 위로 살짝 띄움
        go.transform.localScale = new Vector3(radius * 2f, 0.01f, radius * 2f);

        SkillWarningIndicator indicator = go.AddComponent<SkillWarningIndicator>();
        indicator.Init(color, duration);
        return indicator;
    }

    // 사각형/직선 돌진 장판 생성
    public static SkillWarningIndicator SpawnBox(Vector3 startPos, Vector3 direction, float width, float length, float duration, Color color, LayerMask groundLayer = default)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());

        Vector3 groundStart = GetGroundPosition(startPos, groundLayer);
        Vector3 center = groundStart + direction.normalized * (length * 0.5f);

        go.transform.position = center + Vector3.up * 0.05f;
        go.transform.rotation = Quaternion.LookRotation(direction);
        go.transform.localScale = new Vector3(width, 0.01f, length);

        SkillWarningIndicator indicator = go.AddComponent<SkillWarningIndicator>();
        indicator.Init(color, duration);
        return indicator;
    }

    // 전달받은 위치 아래로 레이를 쏘아 실제 지면(Ground) 높이 Y를 구함
    private static Vector3 GetGroundPosition(Vector3 origin, LayerMask groundLayer)
    {
        Vector3 rayStart = origin + Vector3.up * 10f; // 공중에서 아래로 Ray 발사
        if (groundLayer != 0 && Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, groundLayer))
        {
            return hit.point;
        }
        else if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hitAny, 200f))
        {
            return hitAny.point;
        }
        return origin;
    }

    private void Init(Color color, float duration)
    {
        meshRenderer = GetComponent<MeshRenderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.color = color;

        // URP 런타임 투명 마테리얼 필수 옵션 적용
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);     // Alpha

        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0); // 깊이 버퍼 쓰기 해제 (지면 메시에 파묻혀 안 보이는 현상 방지)
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 렌더 큐 3000(투명) 설정

        meshRenderer.material = mat;

        Destroy(gameObject, duration);
    }
}