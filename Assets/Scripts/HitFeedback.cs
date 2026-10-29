using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 피격 연출: TickToy → 만화 나사 스프라이트 튀김 / AI → 얼굴만 붉어짐(스피어 없음).
/// </summary>
public static class HitFeedback
{
    const int ScrewCount = 12;
    const float ScrewLife = 0.85f;
    const float FaceFlashSeconds = 0.85f;

    static Sprite screwSprite;
    static Texture2D screwTex;

    public static void PlayTickToyHit(Transform target)
    {
        if (target == null)
            return;
        EnsureRunner().StartCoroutine(SpawnCartoonScrews(ResolveHitOrigin(target)));
    }

    public static void PlayAiHit(Transform target)
    {
        if (target == null)
            return;
        EnsureRunner().StartCoroutine(FlashFaceOnly(target));
    }

    static Vector3 ResolveHitOrigin(Transform target)
    {
        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null && col.enabled)
            return col.bounds.center;
        Renderer r = target.GetComponentInChildren<Renderer>();
        if (r != null)
            return r.bounds.center;
        return target.position + Vector3.up * 1f;
    }

    static IEnumerator SpawnCartoonScrews(Vector3 origin)
    {
        EnsureCartoonScrewSprite();
        if (screwSprite == null)
            yield break;

        var pieces = new Transform[ScrewCount];
        var velocities = new Vector3[ScrewCount];
        var spinSpeeds = new float[ScrewCount];
        var baseScale = new float[ScrewCount];
        var sprites = new SpriteRenderer[ScrewCount];

        Camera cam = Camera.main;
        Vector3 camRight = cam != null ? cam.transform.right : Vector3.right;
        Vector3 camUp = cam != null ? cam.transform.up : Vector3.up;

        for (int i = 0; i < ScrewCount; i++)
        {
            float s = Random.Range(0.22f, 0.38f);
            baseScale[i] = s;

            GameObject go = new GameObject("CartoonScrew");
            Vector2 radial = Random.insideUnitCircle.normalized;
            if (radial.sqrMagnitude < 0.01f)
                radial = Vector2.right;
            go.transform.position = origin
                + camRight * (radial.x * 0.08f)
                + camUp * (radial.y * 0.08f + 0.05f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = screwSprite;
            sr.sortingOrder = 80;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.color = Color.white;

            go.transform.localScale = Vector3.zero; // 팝 인
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            // 화면 기준으로 사방 튀기기 (만화 이펙트)
            Vector3 dir = (camRight * radial.x + camUp * (radial.y + 0.55f) + Vector3.up * 0.35f).normalized;
            velocities[i] = dir * Random.Range(2.8f, 4.8f);
            spinSpeeds[i] = Random.Range(-560f, 560f);
            pieces[i] = go.transform;
            sprites[i] = sr;
        }

        float t = 0f;
        Vector3 gravity = new Vector3(0f, -11f, 0f);

        while (t < ScrewLife)
        {
            float dt = Time.deltaTime;
            t += dt;
            float u = Mathf.Clamp01(t / ScrewLife);
            float fade = 1f - u;
            if (cam == null)
                cam = Camera.main;

            // 초반 통통 팝
            float pop = u < 0.12f ? Mathf.SmoothStep(0f, 1.2f, u / 0.12f) : Mathf.Lerp(1.2f, 0.15f, (u - 0.12f) / 0.88f);

            for (int i = 0; i < ScrewCount; i++)
            {
                Transform tr = pieces[i];
                if (tr == null)
                    continue;

                velocities[i] += gravity * dt;
                tr.position += velocities[i] * dt;
                tr.Rotate(0f, 0f, spinSpeeds[i] * dt, Space.Self);

                if (cam != null)
                {
                    Vector3 forward = tr.position - cam.transform.position;
                    if (forward.sqrMagnitude > 0.0001f)
                    {
                        Quaternion billboard = Quaternion.LookRotation(forward.normalized, cam.transform.up);
                        float z = tr.localEulerAngles.z;
                        tr.rotation = billboard * Quaternion.Euler(0f, 0f, z);
                    }
                }

                tr.localScale = Vector3.one * (baseScale[i] * pop);
                if (sprites[i] != null)
                {
                    Color c = Color.white;
                    c.a = fade;
                    sprites[i].color = c;
                }
            }

            yield return null;
        }

        for (int i = 0; i < ScrewCount; i++)
        {
            if (pieces[i] != null)
                Object.Destroy(pieces[i].gameObject);
        }
    }

    /// <summary>스피어 없이 스킨 틴트/emission만으로 얼굴·상체 붉어짐.</summary>
    static IEnumerator FlashFaceOnly(Transform target)
    {
        SkinnedMeshRenderer skin = target.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null)
            yield break;

        var skinBlock = new MaterialPropertyBlock();
        if (skin.sharedMaterial != null)
        {
            skin.sharedMaterial.EnableKeyword("_EMISSION");
            if (skin.sharedMaterial.HasProperty("_EmissionColor"))
                skin.sharedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        Color deepRed = new Color(1f, 0f, 0f, 1f);
        Color emitRed = new Color(5f, 0.05f, 0.05f, 1f);

        float t = 0f;
        while (t < FaceFlashSeconds && target != null && skin != null)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / FaceFlashSeconds);
            float intensity = u < 0.1f
                ? Mathf.SmoothStep(0f, 1f, u / 0.1f)
                : Mathf.SmoothStep(1f, 0f, (u - 0.1f) / 0.9f);

            Color baseTint = Color.Lerp(Color.white, deepRed, intensity * 0.95f);
            Color emit = Color.Lerp(Color.black, emitRed, intensity);
            skinBlock.Clear();
            skinBlock.SetColor("_BaseColor", baseTint);
            skinBlock.SetColor("_Color", baseTint);
            skinBlock.SetColor("_EmissionColor", emit);
            skin.SetPropertyBlock(skinBlock);

            yield return null;
        }

        if (skin != null)
            skin.SetPropertyBlock(null);
    }

    static void EnsureCartoonScrewSprite()
    {
        if (screwSprite != null)
            return;

        const int size = 128;
        screwTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        screwTex.filterMode = FilterMode.Bilinear;
        screwTex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.12f, 0.12f, 0.14f, 1f);
        Color metal = new Color(0.78f, 0.82f, 0.88f, 1f);
        Color metalDark = new Color(0.55f, 0.6f, 0.68f, 1f);
        Color highlight = new Color(0.95f, 0.97f, 1f, 1f);

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        // 좌표: 위쪽이 머리, 아래가 몸통
        FillCircle(pixels, size, 64, 88, 28, outline);
        FillCircle(pixels, size, 64, 88, 24, metal);
        FillCircle(pixels, size, 56, 96, 7, highlight);

        // 십자 홈
        FillRect(pixels, size, 52, 84, 24, 6, metalDark);
        FillRect(pixels, size, 61, 76, 6, 24, metalDark);

        // 나사 몸통
        FillRect(pixels, size, 52, 18, 24, 52, outline);
        FillRect(pixels, size, 55, 20, 18, 48, metal);
        // 나사산 줄무늬
        for (int y = 24; y < 64; y += 7)
            FillRect(pixels, size, 55, y, 18, 3, metalDark);

        // 끝 뾰족
        FillTriangleTip(pixels, size, 64, 10, 12, outline);
        FillTriangleTip(pixels, size, 64, 12, 8, metal);

        screwTex.SetPixels(pixels);
        screwTex.Apply(false, false);
        screwSprite = Sprite.Create(screwTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        screwSprite.name = "CartoonScrewSprite";
    }

    static void FillCircle(Color[] px, int size, int cx, int cy, int r, Color col)
    {
        int r2 = r * r;
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= size)
                continue;
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= size)
                    continue;
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy <= r2)
                    px[y * size + x] = col;
            }
        }
    }

    static void FillRect(Color[] px, int size, int x0, int y0, int w, int h, Color col)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            if (y < 0 || y >= size)
                continue;
            for (int x = x0; x < x0 + w; x++)
            {
                if (x < 0 || x >= size)
                    continue;
                px[y * size + x] = col;
            }
        }
    }

    static void FillTriangleTip(Color[] px, int size, int cx, int tipY, int halfW, Color col)
    {
        for (int y = tipY; y < tipY + halfW + 4; y++)
        {
            if (y < 0 || y >= size)
                continue;
            float t = (y - tipY) / (float)(halfW + 3);
            int w = Mathf.RoundToInt(Mathf.Lerp(1f, halfW, t));
            for (int x = cx - w; x <= cx + w; x++)
            {
                if (x < 0 || x >= size)
                    continue;
                px[y * size + x] = col;
            }
        }
    }

    static HitFeedbackRunner EnsureRunner()
    {
        HitFeedbackRunner runner = Object.FindFirstObjectByType<HitFeedbackRunner>();
        if (runner != null)
            return runner;

        GameObject go = new GameObject("HitFeedbackRunner");
        Object.DontDestroyOnLoad(go);
        return go.AddComponent<HitFeedbackRunner>();
    }

    class HitFeedbackRunner : MonoBehaviour
    {
    }
}
