using System.Collections;
using UnityEngine;

public class NPCDialogue : MonoBehaviour
{
[Header("Lines")]
public string[] prePartyLines;
public string[] postPartyLines;
[Header("Voice (optional)")]
public AudioSource voiceSource;         // auto-fills from this GameObject if null
public AudioClip[] prePartyClips;
public AudioClip[] postPartyClips;
public float voiceVolume = 1f;

[Header("Behavior")]
public bool autoChatter = true;
public Vector2 autoIntervalRange = new Vector2(6f, 12f);
public bool requireListenerNearby = true;
public Transform listener;              // auto-fills Player tag if null
public float maxSpeakDistance = 8f;
public bool onlyOneLine = false;        // speak only once ever
public bool allowOverlap = false;       // allow speaking while audio is already playing

[Header("Animation (optional)")]
public Animator animator;
public string talkTriggerName = "Talk";

Coroutine chatterRoutine;
bool spokeOnce;

void Awake()
{
    if (!voiceSource) voiceSource = GetComponent<AudioSource>();
    if (!listener)
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player) listener = player.transform;
    }
}

void OnEnable()
{
    if (autoChatter && chatterRoutine == null)
        chatterRoutine = StartCoroutine(AutoChatter());
}

void OnDisable()
{
    if (chatterRoutine != null)
    {
        StopCoroutine(chatterRoutine);
        chatterRoutine = null;
    }
}

IEnumerator AutoChatter()
{
    while (enabled && gameObject.activeInHierarchy && (!onlyOneLine || !spokeOnce))
    {
        float wait = Random.Range(autoIntervalRange.x, autoIntervalRange.y);
        yield return new WaitForSeconds(wait);

        if (onlyOneLine && spokeOnce) break;

        if (requireListenerNearby && listener)
        {
            float sqrDist = (listener.position - transform.position).sqrMagnitude;
            if (sqrDist > maxSpeakDistance * maxSpeakDistance) continue;
        }

        if (!allowOverlap && voiceSource && voiceSource.isPlaying) continue;

        Speak();
        if (onlyOneLine) break;
    }

    chatterRoutine = null;
}

public void Speak()
{
    string line = GetRandomLine();
    AudioClip clip = GetRandomClip();

    if (animator && !string.IsNullOrEmpty(talkTriggerName))
        animator.SetTrigger(talkTriggerName);

    if (!string.IsNullOrEmpty(line))
        Debug.Log($"{name}: {line}");

    if (voiceSource && clip)
    {
        voiceSource.volume = voiceVolume;
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    spokeOnce = true;
}

public void Interact()
{
    if (!onlyOneLine || !spokeOnce)
        Speak();
}

string GetRandomLine()
{
    var phase = MainRoomMoodController.CurrentPhase;
    var list = phase == PartyPhase.Post ? postPartyLines : prePartyLines;
    if (list == null || list.Length == 0) return string.Empty;
    return list[Random.Range(0, list.Length)];
}

AudioClip GetRandomClip()
{
    var phase = MainRoomMoodController.CurrentPhase;
    var list = phase == PartyPhase.Post ? postPartyClips : prePartyClips;
    if (list == null || list.Length == 0) return null;
    return list[Random.Range(0, list.Length)];
}
}