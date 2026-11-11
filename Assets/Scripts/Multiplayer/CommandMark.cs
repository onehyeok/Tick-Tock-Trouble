using UnityEngine;

/// <summary>명령하기에 걸린 캐릭터 머리 위 "!" (모두의 화면에, 명령 시간 동안). 캐릭터가 바라보는 방향을 향함</summary>
public class CommandMark : MonoBehaviour
{
    static Font font;
    const float Size = 0.1f; // 글자 크기 (클수록 큼)
    Transform target;
    float height;
    float life;

    public static void Attach(GameObject character, float seconds)
    {
        if (character == null)
            return;
        GameObject go = new GameObject("CommandMark");
        go.AddComponent<CommandMark>().Begin(character.transform, seconds);
    }

    void Begin(Transform t, float seconds)
    {
        target = t;
        life = seconds;
        // 머리 높이: 캐릭터 모습(렌더러) 맨 위
        height = 1.2f;
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer)
                continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any)
            height = b.max.y - t.position.y;

        if (font == null)
            font = Resources.Load<Font>("Fonts/Jua-Regular");

        // 입체감: 오른쪽 아래로 진한 그림자(두께) → 갈색 굵은 테두리 → 굵은 빨간 "!"
        Color depth = new Color(0.45f, 0.05f, 0.04f, 1f);
        Color outline = new Color(0.25f, 0.13f, 0.07f, 1f);
        for (int i = 1; i <= 3; i++)
            MakeLayer("Depth", depth, new Vector2(0.012f * i, -0.016f * i), 0);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f;
            MakeLayer("Outline", outline, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.035f, 1);
        }
        MakeLayer("Main", new Color(1f, 0.18f, 0.14f, 1f), Vector2.zero, 2);
        Follow();
    }

    TextMesh MakeLayer(string name, Color color, Vector2 offset, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(offset.x, offset.y, 0.001f * (3 - order)); // 뒤 레이어는 살짝 뒤로
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = "!";
        tm.anchor = TextAnchor.LowerCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 120;
        tm.fontStyle = FontStyle.Bold;
        tm.characterSize = Size;
        tm.color = color;
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (font != null)
        {
            tm.font = font;
            mr.sharedMaterial = font.material;
        }
        mr.sortingOrder = order;
        return tm;
    }

    void LateUpdate()
    {
        life -= Time.deltaTime;
        if (life <= 0f || target == null || !target.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }
        Follow();
    }

    void Follow()
    {
        transform.position = target.position + Vector3.up * (height + 0.25f);
        // 그 캐릭터가 바라보는 방향으로 (캐릭터 앞에서 보면 정면)
        Vector3 facing = target.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(-facing.normalized, Vector3.up);
    }
}
