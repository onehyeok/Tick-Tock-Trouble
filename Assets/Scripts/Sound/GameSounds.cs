using UnityEngine;

/// <summary>게임 소리 번호 (네트워크로 이 번호만 보냄). 소리를 추가할 때 여기에 번호를 하나 더 붙임</summary>
public enum SoundId : byte
{
    None = 0,
    PlayPossum = 1, // 틱토이: 기절한 척하기
}

/// <summary>누가 듣는 소리인지</summary>
public enum SoundScope : byte
{
    Private = 0, // 나 + 나를 관전하는 사람만 (틱토이 능력 소리)
    All = 1,     // 모두
}

/// <summary>소리 목록 (Assets/Resources/GameSounds.asset) — 번호 ↔ 소리 파일</summary>
public class GameSounds : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public SoundId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    public Entry[] sounds;

    static GameSounds instance;

    public static GameSounds Load()
    {
        if (instance == null)
            instance = Resources.Load<GameSounds>("GameSounds");
        return instance;
    }

    public Entry Get(SoundId id)
    {
        if (sounds == null)
            return null;
        foreach (Entry e in sounds)
        {
            if (e != null && e.id == id)
                return e;
        }
        return null;
    }
}
