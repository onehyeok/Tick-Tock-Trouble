using UnityEngine;

public static class ArmpitSeparator
{
    public static void Apply(Animator animator)
    {
        if (animator == null || !animator.isHuman)
            return;

        SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            Separate(animator, renderers[i]);
    }

    static void Separate(Animator animator, SkinnedMeshRenderer renderer)
    {
        Mesh source = renderer.sharedMesh;
        if (source == null || !source.isReadable || source.name.EndsWith(" ArmpitSeparated"))
            return;

        Transform[] skinBones = renderer.bones;
        BoneWeight[] sourceWeights = source.boneWeights;
        Matrix4x4[] bind = source.bindposes;
        if (skinBones == null || sourceWeights == null || bind == null)
            return;
        if (skinBones.Length == 0 || sourceWeights.Length == 0 || bind.Length != skinBones.Length)
            return;

        int[] kind = new int[skinBones.Length];
        Mark(animator, skinBones, kind, HumanBodyBones.Hips, 1);
        Mark(animator, skinBones, kind, HumanBodyBones.Spine, 1);
        Mark(animator, skinBones, kind, HumanBodyBones.Chest, 1);
        Mark(animator, skinBones, kind, HumanBodyBones.UpperChest, 1);
        Mark(animator, skinBones, kind, HumanBodyBones.LeftShoulder, 2);
        Mark(animator, skinBones, kind, HumanBodyBones.RightShoulder, 2);
        Mark(animator, skinBones, kind, HumanBodyBones.LeftUpperArm, 2);
        Mark(animator, skinBones, kind, HumanBodyBones.RightUpperArm, 2);
        Mark(animator, skinBones, kind, HumanBodyBones.LeftLowerArm, 2);
        Mark(animator, skinBones, kind, HumanBodyBones.RightLowerArm, 2);

        Vector3 leftJoint;
        Vector3 rightJoint;
        float leftRadius;
        float rightRadius;
        if (!JointRadius(animator, skinBones, bind, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftLowerArm, out leftJoint, out leftRadius))
            return;
        if (!JointRadius(animator, skinBones, bind, HumanBodyBones.RightShoulder, HumanBodyBones.RightLowerArm, out rightJoint, out rightRadius))
            return;

        Mesh mesh = Object.Instantiate(source);
        mesh.name = source.name + " ArmpitSeparated";
        mesh.hideFlags = HideFlags.DontSave;
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;

        int count = Mathf.Min(weights.Length, vertices.Length);
        for (int i = 0; i < count; i++)
        {
            float left = Vector3.Distance(vertices[i], leftJoint);
            float right = Vector3.Distance(vertices[i], rightJoint);
            if (left > leftRadius && right > rightRadius)
                continue;

            float torso = WeightOf(weights[i], kind, 1);
            float arm = WeightOf(weights[i], kind, 2);
            if (torso < 0.12f || arm < 0.12f)
                continue;

            int drop = arm >= torso ? 1 : 2;
            weights[i] = DropKind(weights[i], kind, drop);
        }

        mesh.boneWeights = weights;
        renderer.sharedMesh = mesh;
    }

    static void Mark(Animator animator, Transform[] skinBones, int[] kind, HumanBodyBones bone, int value)
    {
        Transform found = animator.GetBoneTransform(bone);
        if (found == null)
            return;

        for (int i = 0; i < skinBones.Length; i++)
        {
            if (skinBones[i] == found)
                kind[i] = value;
        }
    }

    static bool JointRadius(Animator animator, Transform[] skinBones, Matrix4x4[] bind, HumanBodyBones shoulderBone, HumanBodyBones elbowBone, out Vector3 joint, out float radius)
    {
        joint = Vector3.zero;
        radius = 0f;
        int shoulder = IndexOf(skinBones, animator.GetBoneTransform(shoulderBone));
        int elbow = IndexOf(skinBones, animator.GetBoneTransform(elbowBone));
        if (shoulder < 0 || shoulder >= bind.Length)
            return false;

        joint = bind[shoulder].inverse.MultiplyPoint3x4(Vector3.zero);
        float length = 0.2f;
        if (elbow >= 0 && elbow < bind.Length)
            length = Vector3.Distance(joint, bind[elbow].inverse.MultiplyPoint3x4(Vector3.zero));

        radius = Mathf.Max(0.08f, length * 0.75f);
        return true;
    }

    static int IndexOf(Transform[] bones, Transform target)
    {
        if (target == null)
            return -1;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == target)
                return i;
        }

        return -1;
    }

    static int Slot(int boneIndex, int[] kind)
    {
        if (boneIndex < 0 || boneIndex >= kind.Length)
            return 0;

        return kind[boneIndex];
    }

    static float WeightOf(BoneWeight weight, int[] kind, int want)
    {
        float sum = 0f;
        if (Slot(weight.boneIndex0, kind) == want)
            sum += weight.weight0;
        if (Slot(weight.boneIndex1, kind) == want)
            sum += weight.weight1;
        if (Slot(weight.boneIndex2, kind) == want)
            sum += weight.weight2;
        if (Slot(weight.boneIndex3, kind) == want)
            sum += weight.weight3;
        return sum;
    }

    static BoneWeight DropKind(BoneWeight weight, int[] kind, int drop)
    {
        if (Slot(weight.boneIndex0, kind) == drop)
            weight.weight0 = 0f;
        if (Slot(weight.boneIndex1, kind) == drop)
            weight.weight1 = 0f;
        if (Slot(weight.boneIndex2, kind) == drop)
            weight.weight2 = 0f;
        if (Slot(weight.boneIndex3, kind) == drop)
            weight.weight3 = 0f;

        float sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
        if (sum < 0.001f)
            return weight;

        weight.weight0 /= sum;
        weight.weight1 /= sum;
        weight.weight2 /= sum;
        weight.weight3 /= sum;
        return weight;
    }
}
