using System.Collections.Generic;
using UnityEngine;

/// <summary>덫 속박용 빨간 테두리. 스텐실로 몸통을 제외해 테두리만, 벽 뒤에서도 표시.</summary>
public static class TrapOutlineUtil
{
    static Material maskMaterial;
    static Material edgeMaterial;

    public static void Apply(Transform root)
    {
        if (root == null)
            return;

        Clear(root);
        Material mask = GetMaskMaterial();
        Material edge = GetEdgeMaterial();
        TrapOutlineMarker marker = root.gameObject.AddComponent<TrapOutlineMarker>();

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
                continue;

            Material[] original = r.sharedMaterials;
            if (original == null || original.Length == 0)
                continue;

            Material[] next = new Material[original.Length + 2];
            for (int m = 0; m < original.Length; m++)
                next[m] = original[m];
            next[original.Length] = mask;
            next[original.Length + 1] = edge;

            marker.Renderers.Add(r);
            marker.Originals.Add(original);
            r.sharedMaterials = next;
        }
    }

    public static void Clear(Transform root)
    {
        if (root == null)
            return;

        TrapOutlineMarker[] markers = root.GetComponentsInChildren<TrapOutlineMarker>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            TrapOutlineMarker marker = markers[i];
            if (marker == null)
                continue;

            for (int r = 0; r < marker.Renderers.Count; r++)
            {
                Renderer renderer = marker.Renderers[r];
                if (renderer == null)
                    continue;
                if (r < marker.Originals.Count && marker.Originals[r] != null)
                    renderer.sharedMaterials = marker.Originals[r];
            }

            Object.Destroy(marker);
        }
    }

    static Material GetMaskMaterial()
    {
        Shader shader = Shader.Find("TickTock/TrapOutlineMask");
        if (maskMaterial != null && maskMaterial.shader == shader)
            return maskMaterial;
        if (maskMaterial != null)
            Object.Destroy(maskMaterial);

        maskMaterial = new Material(shader);
        maskMaterial.name = "TrapOutlineMask";
        maskMaterial.renderQueue = 4999;
        return maskMaterial;
    }

    static Material GetEdgeMaterial()
    {
        Shader shader = Shader.Find("TickTock/TrapOutlineEdge");
        if (edgeMaterial != null && edgeMaterial.shader == shader)
            return edgeMaterial;
        if (edgeMaterial != null)
            Object.Destroy(edgeMaterial);

        edgeMaterial = new Material(shader);
        edgeMaterial.name = "TrapOutlineEdge";
        edgeMaterial.renderQueue = 5000;
        edgeMaterial.SetColor("_OutlineColor", new Color(1f, 0.05f, 0.05f, 1f));
        edgeMaterial.SetFloat("_OutlineWidth", 0.035f);
        return edgeMaterial;
    }
}

/// <summary>외곽선 적용 시 원본 머티리얼 복원용.</summary>
public class TrapOutlineMarker : MonoBehaviour
{
    public readonly List<Renderer> Renderers = new List<Renderer>();
    public readonly List<Material[]> Originals = new List<Material[]>();
}
