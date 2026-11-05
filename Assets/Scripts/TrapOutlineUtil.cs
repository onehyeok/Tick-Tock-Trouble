using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 덫 속박용 빨간 테두리 — 틱토이 전체 외형(실루엣) 바깥쪽만 빨갛게.
/// 각 렌더러를 복사한 "가림막(스텐실)"을 먼저 전부 그리고, 그 다음 살짝 부풀린 "테두리"를 가림막 바깥에만 그림.
/// 예전엔 원래 렌더러에 머티리얼을 덧붙여서, 부분이 여러 개(서브메시)인 메시는 마지막 부분(눈·입 등)에만
/// 테두리가 생겨 얼굴에 빨간 선이 이상하게 보였음 → 서브메시마다 다 그리도록 복사본 사용.
/// </summary>
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

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;

            if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                marker.Copies.Add(CopySkinned(skinned, mask, "TrapOutlineMask"));
                marker.Copies.Add(CopySkinned(skinned, edge, "TrapOutlineEdge"));
            }
            else if (r is MeshRenderer)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;
                marker.Copies.Add(CopyMesh(r, mf.sharedMesh, mask, "TrapOutlineMask"));
                marker.Copies.Add(CopyMesh(r, mf.sharedMesh, edge, "TrapOutlineEdge"));
            }
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
            for (int c = 0; c < marker.Copies.Count; c++)
            {
                if (marker.Copies[c] != null)
                    Object.Destroy(marker.Copies[c]);
            }
            Object.Destroy(marker);
        }
    }

    static Material[] Fill(Material m, int count)
    {
        Material[] mats = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = m;
        return mats;
    }

    static GameObject CopySkinned(SkinnedMeshRenderer src, Material mat, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(src.transform, false);
        SkinnedMeshRenderer copy = go.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = src.sharedMesh;
        copy.bones = src.bones;
        copy.rootBone = src.rootBone;
        copy.localBounds = src.localBounds;
        copy.updateWhenOffscreen = src.updateWhenOffscreen;
        for (int b = 0; b < src.sharedMesh.blendShapeCount; b++)
            copy.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));
        copy.sharedMaterials = Fill(mat, src.sharedMesh.subMeshCount);
        copy.shadowCastingMode = ShadowCastingMode.Off;
        copy.receiveShadows = false;
        return go;
    }

    static GameObject CopyMesh(Renderer src, Mesh mesh, Material mat, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(src.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer copy = go.AddComponent<MeshRenderer>();
        copy.sharedMaterials = Fill(mat, mesh.subMeshCount);
        copy.shadowCastingMode = ShadowCastingMode.Off;
        copy.receiveShadows = false;
        return go;
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

/// <summary>외곽선 복사본 정리용.</summary>
public class TrapOutlineMarker : MonoBehaviour
{
    public readonly List<GameObject> Copies = new List<GameObject>();
}
