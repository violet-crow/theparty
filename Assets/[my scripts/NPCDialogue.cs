using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[System.Serializable]
public class DialogueEntry
{
public AudioClip clip;
[TextArea] public string subtitles;
public bool isPostParty; // Check this to mark the entry as post-party only
}

public class NPCDialogue : MonoBehaviour
{
[Header("Setup")]
public AudioSource audioSource;
public GameObject talkPrompt; // world-space "(A) Talk" prefab
[Header("Legacy single-line (optional)")]
public AudioClip clip;
[TextArea] public string subtitleText;

[Header("Multi-line dialogue")]
public List<DialogueEntry> dialogues = new List<DialogueEntry>();
public bool randomize = false;           // pick a random line instead of cycling
public bool avoidImmediateRepeat = true; // try not to repeat the last line when randomizing

[Header("Behavior")]
public float showPromptDistance = 2.0f; // meters
public bool oneShot = false;            // optional: only talk once (after first interaction)

[Header("Prompt/interaction gating")]
[Range(0f, 1f)] public float lookDotThreshold = 0.6f; // 0.6 ≈ 53°, 0.8 ≈ 36°

[Header("Dialogue phase")]
public bool isPostPartyContext = false; // Set true after the party to use post-party lines

Transform playerHead;
bool clipPlaying;

// Cycling indices maintained per phase (pre/post party)
int nextIndexPre = 0;
int nextIndexPost = 0;

// Track the last entry played to avoid immediate repeats in random mode
DialogueEntry lastPlayedEntry = null;

// Buffer for eligible entries per current phase
readonly List<DialogueEntry> eligibleBuffer = new List<DialogueEntry>();
int lastEligibleCount = 0;

float lastDialogueDuration = 0f;

void Start()
{
    if (!audioSource) audioSource = GetComponent<AudioSource>();
    if (talkPrompt) talkPrompt.SetActive(false);
    playerHead = Camera.main ? Camera.main.transform : null;
}

void Update()
{
    // Find player head
    if (!playerHead && Camera.main) playerHead = Camera.main.transform;
    if (!playerHead) return;

    // Proximity
    float dist = Vector3.Distance(playerHead.position, transform.position);
    bool inRange = dist <= showPromptDistance && !clipPlaying;

    // Looking check (full 3D)
    bool looking = false;
    if (inRange)
    {
        Vector3 toNPC = (transform.position - playerHead.position).normalized;
        float dot = Vector3.Dot(playerHead.forward, toNPC);
        looking = dot >= lookDotThreshold;
    }

    // Only prompt if there is something eligible to say right now
    bool hasContent = HasEligibleContentForCurrentPhase();
    bool canPromptAndInteract = inRange && looking && hasContent;

    if (talkPrompt) talkPrompt.SetActive(canPromptAndInteract);

    if (canPromptAndInteract && GetRightAButtonDown())
        PlayDialogue();
}

bool GetRightAButtonDown()
{
    // Oculus A button (right controller)
    return OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
}

void PlayDialogue()
{
    DialogueEntry entry = SelectDialogueEntry();

    // If no eligible entries and no legacy clip/text, do nothing
    if (entry == null && clip == null && string.IsNullOrEmpty(subtitleText))
        return;

    clipPlaying = true;
    if (talkPrompt) talkPrompt.SetActive(false);

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

        // Track last played for repeat avoidance
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
    int idx;
    if (lastEligibleCount == 0)
        return null;

    if (!randomize)
    {
        idx = isPostPartyContext ? nextIndexPost : nextIndexPre;
        // Clamp in case the count changed since last time
        if (idx < 0 || idx >= lastEligibleCount) idx = 0;
        return eligibleBuffer[idx];
    }

    // Random selection
    if (lastEligibleCount == 1)
        return eligibleBuffer[0];

    idx = Random.Range(0, lastEligibleCount);

    if (avoidImmediateRepeat && lastPlayedEntry != null)
    {
        const int attempts = 5;
        int tries = 0;
        while (eligibleBuffer[idx] == lastPlayedEntry && tries < attempts)
        {
            idx = Random.Range(0, lastEligibleCount);
            tries++;
        }
    }

    return eligibleBuffer[idx];
}

bool HasEligibleContentForCurrentPhase()
{
    // If any eligible dialogue exists for the current phase, we can prompt
    if (dialogues != null && dialogues.Count > 0)
    {
        for (int i = 0; i < dialogues.Count; i++)
        {
            DialogueEntry d = dialogues[i];
            if (d != null && d.isPostParty == isPostPartyContext)
                return true;
        }
    }

    // Otherwise, allow legacy single-line if present
    if (clip != null) return true;
    if (!string.IsNullOrEmpty(subtitleText)) return true;

    return false;
}

IEnumerator WaitForDialogueEnd()
{
    // If there is audio, wait for it to finish; otherwise wait for the subtitle duration
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
        // Optional: disable interaction after first play
        enabled = false;
        if (talkPrompt) talkPrompt.SetActive(false);
    }
}
}