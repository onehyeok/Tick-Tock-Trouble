using UnityEngine;

/// <summary>명령하기 범위: 땅 위 빨간 원 테두리 + 연한 빨간 반투명 바닥 (명령 시간 동안 보이고 끝날 때 사라짐)</summary>
public class CommandCircle : MonoBehaviour
{
    const int Segments = 72;
    const float LineAlpha = 1f;
    const float FillAlpha = 0.22f;
    static Material material;
    LineRenderer line;
    Mesh fillMesh;
    Color[] fillColors;
    float life;
    float total;

    public void Begin(float radius, float seconds)
    {
        total = life = seconds;
        if (material == null)
            material = new Material(Shader.Find("Sprites/Default")); // 정점 색·투명도 그대로 표시

        // 땅 높이에 맞춘 둘레 점 (울퉁불퉁한 곳도 땅에 붙게)
        Vector3 c = transform.position;
        Vector3[] ring = new Vector3[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            Vector3 p = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            ring[i] = p + Vector3.up * 0.06f;
        }

        // 테두리
        line = gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = Segments;
        line.SetPositions(ring);
        line.widthMultiplier = 0.15f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        // 바닥: 가운데 점 + 둘레 점으로 부채꼴 메쉬
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(transform, false);
        fill.transform.position = Vector3.zero;
        // 가운데 높이 = 둘레 땅 높이의 평균 (가운데엔 와인더 몸이 있어서 위에서 쏘면 몸에 맞아 천막처럼 솟았음)
        float avgY = 0f;
        for (int i = 0; i < Segments; i++)
            avgY += ring[i].y;
        Vector3 center = new Vector3(c.x, avgY / Segments, c.z);
        Vector3[] verts = new Vector3[Segments + 1];
        verts[0] = center - Vector3.up * 0.01f;
        for (int i = 0; i < Segments; i++)
            verts[i + 1] = ring[i] - Vector3.up * 0.01f;
        int[] tris = new int[Segments * 3];
        for (int i = 0; i < Segments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = 1 + (i + 1) % Segments;
            tris[i * 3 + 2] = 1 + i;
        }
        fillMesh = new Mesh { name = "CommandFill" };
        fillMesh.vertices = verts;
        fillMesh.triangles = tris;
        fillColors = new Color[verts.Length];
        fillMesh.RecalculateBounds();
        fill.AddComponent<MeshFilter>().sharedMesh = fillMesh;
        MeshRenderer mr = fill.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        SetAlpha(1f);
    }

    void SetAlpha(float k)
    {
        Color edge = new Color(1f, 0.15f, 0.12f, LineAlpha * k);
        line.startColor = edge;
        line.endColor = edge;
        Color fill = new Color(1f, 0.35f, 0.32f, FillAlpha * k);
        for (int i = 0; i < fillColors.Length; i++)
            fillColors[i] = fill;
        fillMesh.colors = fillColors;
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f)
        {
            Destroy(fillMesh);
            Destroy(gameObject);
            return;
        }
        // 마지막 0.5초 동안 서서히 사라짐
        SetAlpha(Mathf.Clamp01(life / Mathf.Min(0.5f, total)));
    }
}
