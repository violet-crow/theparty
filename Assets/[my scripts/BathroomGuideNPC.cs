using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class BathroomGuideNPC : MonoBehaviour
{
public MainRoomMoodController mainRoomMood;
[Header("References")]
public Transform player;
public Transform bathroomWaypoint;

[Header("Timing")]
public float initialDelay = 180f;

[Header("Movement")]
public float speed = 1.8f;
public float approachDistance = 1.5f;
public float repathInterval = 0.25f;
public float arrivalThreshold = 0.2f;

[Header("NPCDialogue")]
public NPCDialogue dialogueComponent;

[Header("Special Dialogue (subtitled)")]
public AudioClip approachClip;         // replaces approachDialogue.clip
public string approachSubtitles;       // replaces approachDialogue.subtitles
public AudioClip bathroomClip;         // replaces bathroomDialogue.clip
public string bathroomSubtitles;       // replaces bathroomDialogue.subtitles
public float defaultSubtitleDuration = 3f;

[Header("Audio")]
public AudioSource audioSource;
public AudioClip approachLine;         // fallback if approachClip is null
public AudioClip vomitingSfx;
public bool loopVomiting = true;

[Header("Visuals")]
public SpriteRenderer spriteRenderer;
public Sprite normalSprite;
public Sprite vomitingSprite;

[Header("Animator (optional)")]
public Animator animator;
public string speedParameter = "speed";
public string vomitingTrigger = "Vomit";

public enum PlayerArrivalMode { ByDistance, ByTrigger }
[Header("Player arrival gating")]
public PlayerArrivalMode arrivalMode = PlayerArrivalMode.ByDistance;

// Distance mode
public float bathroomArrivalRadius = 2.5f;

// Trigger mode
public PlayerPresenceTrigger bathroomPresenceTrigger;

private NavMeshAgent agent;

void Awake()
{
    agent = GetComponent<NavMeshAgent>();
    if (!agent)
    {
        Debug.LogError($"{name}: BathroomGuideNPC requires a NavMeshAgent.");
        enabled = false;
        return;
    }

    agent.speed = speed;
    agent.autoBraking = true;
    agent.stoppingDistance = 0f;
    agent.updateRotation = false;

    if (spriteRenderer && normalSprite)
        spriteRenderer.sprite = normalSprite;
}

void OnEnable()
{
    StartCoroutine(RunSequence());
}

IEnumerator RunSequence()
{
    yield return new WaitForSeconds(initialDelay);

    if (dialogueComponent)
        dialogueComponent.enabled = false;

    yield return ApproachPlayer();

    yield return PlaySpecialDialogue(approachClip, approachSubtitles, approachLine);

    if (bathroomWaypoint)
        yield return GoTo(bathroomWaypoint.position);

    agent.isStopped = true;
    agent.ResetPath();
    if (animator) animator.SetFloat(speedParameter, 0f);
    EnterVomitingVisuals();

    yield return new WaitUntil(() => IsPlayerInBathroom());

    if (mainRoomMood) mainRoomMood.Activate();

    yield return PlaySpecialDialogue(bathroomClip, bathroomSubtitles, null);

    PlayVomitingSfx();

    while (true) yield return null;
}

IEnumerator ApproachPlayer()
{
    if (!player) yield break;

    agent.isStopped = false;
    float nextRepath = 0f;

    while (true)
    {
        if (!player) yield break;

        if (Time.time >= nextRepath)
        {
            agent.SetDestination(player.position);
            nextRepath = Time.time + repathInterval;
        }

        if (animator) animator.SetFloat(speedParameter, agent.velocity.magnitude);

        if (!agent.pathPending && Vector3.Distance(transform.position, player.position) <= approachDistance)
            break;

        yield return null;
    }

    FaceTargetFlat(player.position);
}

IEnumerator GoTo(Vector3 worldPos)
{
    agent.isStopped = false;
    agent.SetDestination(worldPos);

    while (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + arrivalThreshold)
    {
        if (animator) animator.SetFloat(speedParameter, agent.velocity.magnitude);
        yield return null;
    }
}

void EnterVomitingVisuals()
{
    if (animator && !string.IsNullOrEmpty(vomitingTrigger))
    {
        animator.SetTrigger(vomitingTrigger);
    }
    else if (spriteRenderer && vomitingSprite)
    {
        spriteRenderer.sprite = vomitingSprite;
    }
}

void PlayVomitingSfx()
{
    if (audioSource && vomitingSfx)
    {
        audioSource.loop = loopVomiting;
        audioSource.clip = vomitingSfx;
        audioSource.Play();
    }
}

bool IsPlayerInBathroom()
{
    if (!player) return false;

    if (arrivalMode == PlayerArrivalMode.ByTrigger)
    {
        return bathroomPresenceTrigger && bathroomPresenceTrigger.IsPlayerInside;
    }
    else
    {
        if (!bathroomWaypoint) return false;
        Vector3 a = bathroomWaypoint.position; a.y = 0f;
        Vector3 b = player.position; b.y = 0f;
        return Vector3.Distance(a, b) <= bathroomArrivalRadius;
    }
}

void FaceTargetFlat(Vector3 targetPos)
{
    Vector3 dir = targetPos - transform.position;
    dir.y = 0f;
    if (dir.sqrMagnitude > 0.0001f)
        transform.rotation = Quaternion.LookRotation(dir);
}

IEnumerator PlaySpecialDialogue(AudioClip clip, string subtitles, AudioClip fallbackClip)
{
    AudioClip chosenClip = clip ? clip : fallbackClip;
    string text = subtitles ?? string.Empty;

    float duration = chosenClip ? chosenClip.length : defaultSubtitleDuration;

    if (audioSource && chosenClip)
    {
        audioSource.loop = false;
        audioSource.clip = chosenClip;
        audioSource.Play();
    }

    if (!string.IsNullOrEmpty(text) && SubtitlesManager.Instance)
        SubtitlesManager.Instance.Show(text, duration);

    if (audioSource && chosenClip)
    {
        yield return new WaitWhile(() => audioSource.isPlaying);
    }
    else if (!string.IsNullOrEmpty(text))
    {
        yield return new WaitForSeconds(duration);
    }
}
}