using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Assets/Model2 안의 FBX를 Humanoid로 자동 설정 (Bolt 모델 + Bolt@Idle/Run/Jump)
public class Model2HumanoidImporter : AssetPostprocessor
{
    const string Folder = "Assets/Model2/";
    const string Marker = "model2-humanoid-v2";

    static bool InFolder(string path) =>
        path.Replace('\\', '/').StartsWith(Folder, System.StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);

    static bool IsAnim(string path) =>
        Path.GetFileName(path).Contains("@") || path.Replace('\\', '/').Contains("/Animations/");

    void OnPreprocessModel()
    {
        if (!InFolder(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        if (mi.userData == Marker) return;

        mi.optimizeBones = false; // 웨이트 없는 본도 유지 (Humanoid 매핑용)
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        bool isAnim = IsAnim(assetPath);
        mi.importAnimation = isAnim;
        if (isAnim) mi.materialImportMode = ModelImporterMaterialImportMode.None;
    }

    void OnPreprocessAnimation()
    {
        if (!InFolder(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        if (mi.userData == Marker) return;
        string file = Path.GetFileNameWithoutExtension(assetPath);
        if (!IsAnim(assetPath)) return;
        string clipName = file.Contains("@") ? file.Substring(file.IndexOf('@') + 1) : file;
        bool loop = clipName != "Jump";

        var clips = mi.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            var c = clips[i];
            c.name = clipName;
            c.loopTime = loop;
            c.loopPose = loop;
            // 제자리 애니메이션: 이동/회전은 스크립트(WASD)가 담당
            c.lockRootRotation = true;      c.keepOriginalOrientation = true;
            c.lockRootHeightY = true;       c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true;    c.keepOriginalPositionXZ = true;
            clips[i] = c;
        }
        mi.clipAnimations = clips;
        mi.userData = Marker;
    }

    void OnPostprocessModel(GameObject g)
    {
        if (!InFolder(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        bool dirty = false;
        if (!IsAnim(assetPath) && mi.userData != Marker) { mi.userData = Marker; dirty = true; }
        // 안테나가 Eye로 자동 매핑되는 것 방지
        var hd = mi.humanDescription;
        if (hd.human != null && hd.human.Any(h => h.humanName.EndsWith("Eye")))
        {
            hd.human = hd.human.Where(h => !h.humanName.EndsWith("Eye")).ToArray();
            mi.humanDescription = hd;
            dirty = true;
        }
        if (dirty) EditorApplication.delayCall += () => mi.SaveAndReimport();
    }

    // 스크립트보다 FBX가 먼저 임포트된 경우 대비: 한 번 재임포트
    [InitializeOnLoadMethod]
    static void FixExisting()
    {
        EditorApplication.delayCall += () =>
        {
            if (!AssetDatabase.IsValidFolder("Assets/Model2"))
                return; // 폴더가 없으면 할 일 없음 (경고 방지)
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Model2" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is ModelImporter mi && mi.userData != Marker)
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
        };
    }
}
