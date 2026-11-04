using UnityEngine;

/// <summary>게임에서 스폰할 캐릭터 프리팹 (Resources/MatchPrefabs)</summary>
[CreateAssetMenu(menuName = "TTT/Match Prefabs", fileName = "MatchPrefabs")]
public class MatchPrefabs : ScriptableObject
{
    public GameObject winderPrefab;
    public GameObject[] tickToyPrefabs;
    /// <summary>와인더 시작 방 (Models/WinderStartingPosition) — 맵에 없으면 이걸 놓음</summary>
    public GameObject winderStartRoom;

    static MatchPrefabs cached;

    public static MatchPrefabs Load()
    {
        if (cached == null)
            cached = Resources.Load<MatchPrefabs>("MatchPrefabs");
        return cached;
    }
}
