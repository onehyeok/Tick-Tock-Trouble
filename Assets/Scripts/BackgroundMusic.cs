using System.Collections;
using System.Linq;
using UnityEngine;

public class BackgroundMusic : MonoBehaviour
{
    [SerializeField] private float volume = 0.4f;

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
        AudioClip[] playlist = clips.Where(clip => clip != null).OrderBy(clip => clip.name).ToArray();
        if (playlist.Length == 0)
            return;

        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = playlist.Length == 1;
        source.spatialBlend = 0f;
        source.volume = volume;
        StartCoroutine(PlayList(source, playlist));
    }

    IEnumerator PlayList(AudioSource source, AudioClip[] playlist)
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
