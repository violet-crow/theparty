using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainRoomMoodController : MonoBehaviour
{
[Header("Teleport object")]
public GameObject teleportObject; // assign in Inspector (start inactive)
public bool enableTeleportObjectOnActivate = true;
[Header("Lights")]
public Light[] ceilingLights;
public Color ceilingLightTargetColor = Color.red;
public float lightFadeDuration = 1.0f;

[Header("Audio")]
public AudioSource musicSource;         // main music
public AudioSource partyAmbientSource;  // current party ambience
public AudioSource fightAmbientSource;  // alternate ambience (fighting)
public float audioFadeDuration = 1.0f;

[Header("Guest culling")]
[Range(0f, 1f)]
public float guestCullPercent = 0.5f;
[Tooltip("Guests to potentially disable. If empty, will try to gather from guestsRoot or tag.")]
public List<GameObject> guestObjects = new List<GameObject>();
public Transform guestsRoot;            // optional: parent that holds guest NPCs
public string guestTag;                 // optional: tag to collect guests at runtime

[Header("Dialogue context")]
public bool setDialogueToPostParty = true;
public bool setDialogueContextAfterTransition = true; // true = flip after fades/culls finish; false = flip immediately

[Header("Execution")]
public bool triggerOnce = true;
private bool alreadyTriggered;

public void Activate()
{
    if (triggerOnce && alreadyTriggered) return;
    alreadyTriggered = true;

    // Enable the interactive object after bathroom trigger
    if (enableTeleportObjectOnActivate && teleportObject)
        teleportObject.SetActive(true);

    // Optionally flip dialogue context right away
    if (setDialogueToPostParty && !setDialogueContextAfterTransition)
        SetDialogueContext(true);

    // Lazy collection if needed
    if (guestObjects.Count == 0)
    {
        if (guestsRoot)
        {
            foreach (Transform child in guestsRoot)
                guestObjects.Add(child.gameObject);
        }
        else if (!string.IsNullOrEmpty(guestTag))
        {
            var found = GameObject.FindGameObjectsWithTag(guestTag);
            guestObjects.AddRange(found);
        }
    }

    StartCoroutine(Run());
}

IEnumerator Run()
{
    // Start audio crossfade and visual changes in parallel
    var tasks = new List<Coroutine>();

    tasks.Add(StartCoroutine(FadeCeilingLights()));
    tasks.Add(StartCoroutine(CrossfadeAudio()));
    tasks.Add(StartCoroutine(CullGuestsGradually()));

    // Wait for all tasks to finish
    foreach (var t in tasks)
        yield return t;

    // Flip dialogue context after transition completes (if configured)
    if (setDialogueToPostParty && setDialogueContextAfterTransition)
        SetDialogueContext(true);
}

void SetDialogueContext(bool postParty)
{
    var npcs = FindObjectsOfType<NPCDialogue>();
    for (int i = 0; i < npcs.Length; i++)
    {
        if (npcs[i]) npcs[i].isPostPartyContext = postParty;
    }
}

IEnumerator FadeCeilingLights()
{
    if (ceilingLights == null || ceilingLights.Length == 0 || lightFadeDuration <= 0f)
        yield break;

    var originals = new Color[ceilingLights.Length];
    for (int i = 0; i < ceilingLights.Length; i++)
    {
        if (ceilingLights[i])
            originals[i] = ceilingLights[i].color;
    }

    float t = 0f;
    while (t < lightFadeDuration)
    {
        float a = t / lightFadeDuration;
        for (int i = 0; i < ceilingLights.Length; i++)
        {
            var l = ceilingLights[i];
            if (l) l.color = Color.Lerp(originals[i], ceilingLightTargetColor, a);
        }
        t += Time.deltaTime;
        yield return null;
    }
    for (int i = 0; i < ceilingLights.Length; i++)
    {
        var l = ceilingLights[i];
        if (l) l.color = ceilingLightTargetColor;
    }
}

IEnumerator CrossfadeAudio()
{
    if (fightAmbientSource)
    {
        if (!fightAmbientSource.isPlaying)
            fightAmbientSource.Play();
        fightAmbientSource.volume = 0f;
        fightAmbientSource.loop = true;
    }

    float startMusic = musicSource ? musicSource.volume : 0f;
    float startParty = partyAmbientSource ? partyAmbientSource.volume : 0f;
    float startFight = fightAmbientSource ? fightAmbientSource.volume : 0f;

    float t = 0f;
    while (t < audioFadeDuration)
    {
        float a = t / audioFadeDuration;
        if (musicSource) musicSource.volume = Mathf.Lerp(startMusic, 0f, a);
        if (partyAmbientSource) partyAmbientSource.volume = Mathf.Lerp(startParty, 0f, a);
        if (fightAmbientSource) fightAmbientSource.volume = Mathf.Lerp(startFight, 1f, a);

        t += Time.deltaTime;
        yield return null;
    }

    if (musicSource)
    {
        musicSource.volume = 0f;
        musicSource.Stop();
    }

    if (partyAmbientSource)
    {
        partyAmbientSource.volume = 0f;
        partyAmbientSource.Stop();
    }

    if (fightAmbientSource)
        fightAmbientSource.volume = 1f;
}

IEnumerator CullGuestsGradually()
{
    if (guestObjects == null || guestObjects.Count == 0 || guestCullPercent <= 0f)
        yield break;

    var candidates = new List<GameObject>();
    foreach (var g in guestObjects)
        if (g) candidates.Add(g);

    int toCull = Mathf.RoundToInt(candidates.Count * guestCullPercent);
    if (toCull <= 0) yield break;

    // Shuffle
    for (int i = candidates.Count - 1; i > 0; i--)
    {
        int j = Random.Range(0, i + 1);
        (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
    }

    int batchSize = Mathf.Clamp(toCull / 5, 1, 20);
    int culled = 0;

    for (int i = 0; i < candidates.Count && culled < toCull; i++)
    {
        var g = candidates[i];
        if (!g) continue;

        // Disable guest object (no destroying)
        g.SetActive(false);

        culled++;

        if (culled % batchSize == 0)
            yield return null;
    }
}
}