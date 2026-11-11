using UnityEngine;

/// <summary>와인더 회복하기: 브릭 주변에 초록 반짝이가 위로 올라가는 이펙트 (모두의 화면에, 약 1.5초)</summary>
public class HealEffect : MonoBehaviour
{
    static Material material;
    Transform follow;

    public static void Spawn(Transform target)
    {
        if (target == null)
            return;
        GameObject go = new GameObject("HealEffect");
        go.transform.position = target.position;
        HealEffect fx = go.AddComponent<HealEffect>();
        fx.follow = target;
        fx.Build();
    }

    void Build()
    {
        ParticleSystem ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer pr = GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.material = GetMaterial();

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 1f, 0.45f, 1f), new Color(0.75f, 1f, 0.6f, 1f));
        main.gravityModifier = -0.25f; // 위로 떠오름
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // 브릭을 따라다님
        main.maxParticles = 120;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 45f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)30) });

        // 브릭 발밑 둘레(반지름 0.8m)에서 위로
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.8f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        transform.position = follow.position + Vector3.up * 0.2f;

        // 점점 투명해짐
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ps.Play();
    }

    void LateUpdate()
    {
        if (follow != null)
            transform.position = follow.position + Vector3.up * 0.2f;
    }

    static Material GetMaterial()
    {
        if (material != null)
            return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "Heal Sparkle", hideFlags = HideFlags.HideAndDontSave };
        Texture2D tex = SoftDot();
        material.mainTexture = tex;
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", tex);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 2f); // 더하기(빛나는 느낌)
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", 5f);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", 1f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = 3000;
        return material;
    }

    /// <summary>가운데가 밝고 가장자리가 흐린 둥근 점</summary>
    static Texture2D SoftDot()
    {
        const int size = 32;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply();
        return t;
    }
}
