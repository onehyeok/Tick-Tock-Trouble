using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 배경음악. 인트로·로비: Resources/BGM 의 원래 노래 / 게임 맵: 들어가자마자 게임 노래(GameSounds 의 GameMusic = TTTGame) 반복.
/// </summary>
public class BackgroundMusic : MonoBehaviour
{
    [SerializeField] private float volume = 0.4f;

    static BackgroundMusic instance;
    AudioSource source;
    AudioClip[] lobbyPlaylist;
    Coroutine lobbyRoutine;
    bool lobbyPlaying;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void PlayFromFolder()
    {
        if (FindAnyObjectByType<BackgroundMusic>() != null)
            return;

        AudioClip[] clips = Resources.LoadAll<AudioClip>("BGM");
        if (clips == null || clips.Length == 0)
            return;

        GameObject player = new GameObject("BackgroundMusic");
        DontDestroyOnLoad(player);
        player.AddComponent<BackgroundMusic>().Begin(clips);
    }

    void Begin(AudioClip[] clips)
    {
        instance = this;
        lobbyPlaylist = clips.Where(clip => clip != null).OrderBy(clip => clip.name).ToArray();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = volume;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameSettings.EnsureLoaded();
        GameSettings.VolumeChanged += ApplyVolume;
        ApplyScene(SceneManager.GetActiveScene());
    }

    float gameClipVolume = 1f;

    /// <summary>ESC 설정의 로비 사운드 / 인게임 사운드 크기 반영</summary>
    void ApplyVolume()
    {
        if (source == null)
            return;
        source.volume = lobbyPlaying
            ? volume * GameSettings.LobbyVolume / 100f
            : volume * gameClipVolume * GameSettings.GameVolume / 100f;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameSettings.VolumeChanged -= ApplyVolume;
        if (instance == this)
            instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            ApplyScene(scene);
    }

    /// <summary>인트로·로비면 원래 노래, 게임 맵이면 게임 노래 (둘 다 이미 나오고 있으면 끊지 않고 이어서)</summary>
    void ApplyScene(Scene scene)
    {
        bool menu = scene.name == MatchService.IntroScene || scene.name == MatchService.LobbyScene;
        if (menu)
        {
            PlayLobby();
            return;
        }
        GameSounds sounds = GameSounds.Load();
        GameSounds.Entry music = sounds != null ? sounds.Get(SoundId.GameMusic) : null;
        if (music != null)
            PlayGameMusic(music.clip, music.volume);
        else
            StopMusic();
    }

    void PlayLobby()
    {
        if (lobbyPlaying || lobbyPlaylist == null || lobbyPlaylist.Length == 0)
            return;
        lobbyPlaying = true;
        ApplyVolume();
        source.loop = lobbyPlaylist.Length == 1;
        lobbyRoutine = StartCoroutine(PlayList(lobbyPlaylist));
    }

    void StopMusic()
    {
        if (lobbyRoutine != null)
            StopCoroutine(lobbyRoutine);
        lobbyRoutine = null;
        lobbyPlaying = false;
        source.Stop();
    }

    /// <summary>게임 맵: 게임 노래 반복 재생 (다음 라운드로 맵을 다시 불러와도 처음부터 다시 틀지 않음)</summary>
    public static void PlayGameMusic(AudioClip clip, float clipVolume)
    {
        if (instance == null || clip == null)
            return;
        if (!instance.lobbyPlaying && instance.source.clip == clip && instance.source.isPlaying)
            return;
        instance.StopMusic();
        instance.source.clip = clip;
        instance.source.loop = true;
        instance.gameClipVolume = clipVolume;
        instance.ApplyVolume();
        instance.source.Play();
    }

    IEnumerator PlayList(AudioClip[] playlist)
    {
        int index = 0;
        while (source != null)
        {
            AudioClip clip = playlist[index];
            source.clip = clip;
            source.Play();
            if (playlist.Length == 1)
                yield break;

            yield return new WaitForSeconds(clip.length);
            index = (index + 1) % playlist.Length;
        }
    }
}
