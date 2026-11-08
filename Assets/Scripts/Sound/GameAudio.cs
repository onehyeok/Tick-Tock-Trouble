using UnityEngine;

/// <summary>
/// 게임 소리 재생 + 온라인 동기화.
/// - Play: 내 화면에서 바로 재생하고 다른 사람에게도 알림
/// - 받는 쪽: Private 소리는 그 사람을 관전 중일 때만, All 소리는 항상 재생
/// </summary>
public static class GameAudio
{
    static AudioSource source;

    public static void Play(SoundId id, SoundScope scope)
    {
        PlayHere(id);
        FusionMatchBackend.Online?.SendSound(id, scope);
    }

    /// <summary>다른 사람(origin)이 낸 소리를 받았을 때</summary>
    public static void PlayRemote(SoundId id, SoundScope scope, int origin)
    {
        if (scope == SoundScope.Private)
        {
            RoundManager rm = RoundManager.Instance;
            if (rm == null || rm.SpectateTargetId != origin)
                return; // 그 사람을 관전 중이 아니면 안 들림
        }
        PlayHere(id);
    }

    /// <summary>내 화면에서만 재생 (모두에게 동시에 일어나는 일 — 예: 게임 시작 — 은 각자 이걸로)</summary>
    public static void PlayHere(SoundId id)
    {
        GameSounds lib = GameSounds.Load();
        GameSounds.Entry e = lib != null ? lib.Get(id) : null;
        if (e == null || e.clip == null)
            return;
        if (source == null)
        {
            GameObject go = new GameObject("GameAudio");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 화면 소리 (거리와 상관없이 같은 크기)
        }
        source.PlayOneShot(e.clip, e.volume);
    }
}
