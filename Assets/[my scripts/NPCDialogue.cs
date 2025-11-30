using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DialogueEntry
{
public AudioClip clip;
[TextArea] public string subtitles;
public bool isPostParty; // true = post-party; false = pre-party
}

public class NPCDialogue : MonoBehaviour
{
[Header("Setup")]
public AudioSource audioSource;
public GameObject talkPrompt;
[Header("Legacy single-line (optional)")]
public AudioClip clip;
[TextArea] public string subtitleText;

[Header("Multi-line dialogue")]
public List<DialogueEntry> dialogues = new List<DialogueEntry>();
public bool randomize = false;
public bool avoidImmediateRepeat = true;

[Header("Behavior")]
public float showPromptDistance = 2.0f;
public bool oneShot = false;

[Header("Prompt/interaction gating")]
[Range(0f, 1f)] public float lookDotThreshold = 0.6f;

[Header("Dialogue phase")]
public bool isPostPartyContext = false;

// Internal state
bool clipPlaying;
int nextIndexPre = 0;
int nextIndexPost = 0;
DialogueEntry lastPlayedEntry = null;
readonly List<DialogueEntry> eligibleBuffer = new List<DialogueEntry>();
int lastEligibleCount = 0;
float lastDialogueDuration = 0f;

void Start()
{
    if (!audioSource) audioSource = GetComponent<AudioSource>();
    if (talkPrompt) talkPrompt.SetActive(false);
}

void OnEnable()
{
    if (DialogueFocusManager.Instance)
        DialogueFocusManager.Instance.Register(this);
}

void OnDisable()
{
    if (DialogueFocusManager.Instance)
        DialogueFocusManager.Instance.Unregister(this);
}

// Manager calls this to decide if the NPC is a candidate and to rank it
public bool ComputeEligibility(Transform head, out float dot, out float dist)
{
    dot = -1f;
    dist = float.MaxValue;

    if (head == null || clipPlaying)
        return false;

    dist = Vector3.Distance(head.position, transform.position);
    if (dist > showPromptDistance)
        return false;

    Vector3 toNPC = (transform.position - head.position).normalized;
    dot = Vector3.Dot(head.forward, toNPC);

    if (dot < lookDotThreshold)
        return false;

    return HasEligibleContentForCurrentPhase();
}

// Manager controls prompt visibility/highlight
public void SetPromptActive(bool active)
{
    if (talkPrompt) talkPrompt.SetActive(active && !clipPlaying);
    // Optional: add visual highlight state changes here
}

// Manager triggers dialogue on the focused NPC
public void TriggerDialogue()
{
    PlayDialogue();
}

public bool IsTalking => clipPlaying;

void PlayDialogue()
{
    DialogueEntry entry = SelectDialogueEntry();

    // If nothing eligible and no legacy clip/text, do nothing
    if (entry == null && clip == null && string.IsNullOrEmpty(subtitleText))
        return;

    clipPlaying = true;
    SetPromptActive(false);

    // Determine audio and duration
    AudioClip chosenClip = entry != null ? entry.clip : clip;
    string chosenSub = entry != null ? entry.subtitles : subtitleText;

    if (chosenClip && audioSource)
    {
        audioSource.clip = chosenClip;
        audioSource.Play();
    }

    lastDialogueDuration = chosenClip ? chosenClip.length : 3f;

    if (!string.IsNullOrEmpty(chosenSub) && SubtitlesManager.Instance)
    {
        SubtitlesManager.Instance.Show(chosenSub, lastDialogueDuration);
    }

    // Advance index for next time (if using multi-line and not random)
    if (entry != null)
    {
        if (!randomize)
        {
            if (isPostPartyContext)
                nextIndexPost = lastEligibleCount > 0 ? (nextIndexPost + 1) % lastEligibleCount : 0;
            else
                nextIndexPre = lastEligibleCount > 0 ? (nextIndexPre + 1) % lastEligibleCount : 0;
        }

        lastPlayedEntry = entry;
    }

    StartCoroutine(WaitForDialogueEnd());
}

DialogueEntry SelectDialogueEntry()
{
    // Build eligible set for current phase
    eligibleBuffer.Clear();
    if (dialogues != null && dialogues.Count > 0)
    {
        for (int i = 0; i < dialogues.Count; i++)
        {
            DialogueEntry d = dialogues[i];
            if (d != null && d.isPostParty == isPostPartyContext)
                eligibleBuffer.Add(d);
        }
    }

    lastEligibleCount = eligibleBuffer.Count;
    if (lastEligibleCount == 0)
        return null;

    // Sequential selection
    if (!randomize)
    {
        int idx = isPostPartyContext ? nextIndexPost : nextIndexPre;
        if (idx < 0 || idx >= lastEligibleCount) idx = 0;
        return eligibleBuffer[idx];
    }

    // Random selection
    if (lastEligibleCount == 1)
        return eligibleBuffer[0];

    int chosen = Random.Range(0, lastEligibleCount);

    if (avoidImmediateRepeat && lastPlayedEntry != null)
    {
        const int attempts = 5;
        for (int tries = 0; tries < attempts && eligibleBuffer[chosen] == lastPlayedEntry; tries++)
            chosen = Random.Range(0, lastEligibleCount);
    }

    return eligibleBuffer[chosen];
}

bool HasEligibleContentForCurrentPhase()
{
    if (dialogues != null && dialogues.Count > 0)
    {
        for (int i = 0; i < dialogues.Count; i++)
        {
            DialogueEntry d = dialogues[i];
            if (d != null && d.isPostParty == isPostPartyContext)
                return true;
        }
    }

    if (clip != null) return true;
    if (!string.IsNullOrEmpty(subtitleText)) return true;

    return false;
}

IEnumerator WaitForDialogueEnd()
{
    if (audioSource && audioSource.clip && audioSource.isPlaying)
    {
        while (audioSource.isPlaying)
            yield return null;
    }
    else
    {
        yield return new WaitForSeconds(lastDialogueDuration);
    }

    clipPlaying = false;

    if (oneShot)
    {
        enabled = false;
        SetPromptActive(false);
    }
}
}