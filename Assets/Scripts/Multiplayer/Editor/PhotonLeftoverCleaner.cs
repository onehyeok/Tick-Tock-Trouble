using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 플레이 시작 직전·종료 직후, 에디터 씬에 남은 Photon 연결 오브젝트(ConnectionHandler / RegionHandler)를 지움.
/// (이게 씬에 남아 있으면 실행할 때마다 "A ConnectionHandler should not be put into a scene" 오류가 뜸)
/// </summary>
[InitializeOnLoad]
static class PhotonLeftoverCleaner
{
    static PhotonLeftoverCleaner()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
                Clean();
        };
    }

    static void Clean()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "ConnectionHandler" || root.name == "RegionHandler")
                    Object.DestroyImmediate(root);
            }
        }
    }
}
