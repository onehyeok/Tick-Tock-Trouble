using UnityEngine;

/// <summary>게임에서 스폰할 캐릭터 프리팹 (Resources/MatchPrefabs)</summary>
[CreateAssetMenu(menuName = "TTT/Match Prefabs", fileName = "MatchPrefabs")]
public class MatchPrefabs : ScriptableObject
{
    public GameObject winderPrefab;
    public GameObject[] tickToyPrefabs;

    static MatchPrefabs cached;

    public static MatchPrefabs Load()
    {
        if (cached == null)
            cached = Resources.Load<MatchPrefabs>("MatchPrefabs");
        return cached;
    }
}
