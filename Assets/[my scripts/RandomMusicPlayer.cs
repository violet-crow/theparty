using UnityEngine;

public class RandomMusicPlayer : MonoBehaviour
{
    public AudioClip[] tracks;
    private AudioSource source;
    private int lastIndex = -1;
    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.loop = false;        // let songs finish
        source.playOnAwake = false; // we’ll start via script
        source.spatialBlend = 0f;   // 2D for background music
    }

    void Start()
    {
        PlayRandom();
    }

    void Update()
    {
        if (!source.isPlaying) PlayRandom();
    }

    void PlayRandom()
    {
        if (tracks == null || tracks.Length == 0) return;
        int i = Random.Range(0, tracks.Length);
        if (tracks.Length > 1 && i == lastIndex) i = (i + 1) % tracks.Length; // avoid instant repeat
        lastIndex = i;
        source.clip = tracks[i];
        source.Play();
    }
}