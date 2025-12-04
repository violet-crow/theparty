using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Autohand;

[RequireComponent(typeof(Grabbable))]
[DisallowMultipleComponent]
public class HoldToTeleport : MonoBehaviour
{
[Header("Teleport")]
[Tooltip("Where the player's head (camera) should end up. Place this transform at the desired head position and facing.")]
public Transform teleportDestination;
[Tooltip("Root transform to move for the player rig (e.g., OVRPlayerController or OVRCameraRig root). If left empty, it will auto-resolve from the main camera.")]
public Transform rigRoot;
[Header("Timing")]
[Tooltip("Seconds the object must be held continuously to trigger teleport.")]
public float holdDuration = 1f;

[Header("Behavior")]
[Tooltip("Rotate the player so their head forward matches the waypoint's forward (planar).")]
public bool alignYawToDestination = true;

[Header("Disable on teleport")]
[Tooltip("This GameObject will be SetActive(false) when teleportation completes.")]
public GameObject disableOnTeleport;

[Tooltip("Allow only a single teleport from this object.")]
public bool onlyOnce = false;

[Tooltip("Release the object from the hand(s) after teleport by briefly disabling colliders.")]
public bool releaseAfterTeleport = true;

[Tooltip("Disable the object after teleport.")]
public bool deactivateAfterTeleport = false;

[Tooltip("How long to disable colliders to force a drop.")]
public float dropDisableDuration = 0.05f;

[Header("Audio")]
[Tooltip("The eerie ambience AudioSource to fade out.")]
public AudioSource eerieAmbiance;
[Tooltip("The calm ambience AudioSource to fade in.")]
public AudioSource calmAmbiance;
[Tooltip("Seconds to crossfade between eerie and calm.")]
public float audioCrossfadeDuration = 2f;
[Tooltip("Stop the eerie source when it reaches 0 volume.")]
public bool stopEerieWhenDone = true;
[Tooltip("Start calm ambience if it is not already playing when the crossfade begins.")]
public bool startCalmIfNotPlaying = true;

[Tooltip("Optional: auto-find these GameObjects by name if the AudioSource fields are left empty.")]
public string eerieAmbianceObjectName = "eerieambiance";
public string calmAmbianceObjectName = "calmambiance";

[Header("Mixer")]
[Tooltip("AudioMixer containing the snapshot you want to switch to on teleport.")]
public AudioMixer mixer;
[Tooltip("Snapshot to switch to on teleport. If left null, the script will try to find it by name in the mixer.")]
public AudioMixerSnapshot partyAfterSnapshot;
[Tooltip("Name of the snapshot to find on the mixer if no snapshot asset is assigned.")]
public string partyAfterSnapshotName = "PartyAfter";
[Tooltip("Seconds to transition to the mixer snapshot.")]
public float snapshotTransitionTime = 0.5f;

[Header("Debug")]
public bool debugLogs = false;

private Grabbable grabbable;
private readonly HashSet<Hand> holdingHands = new HashSet<Hand>();
private Coroutine holdRoutine;
private bool hasTeleported;

void Awake()
{
    grabbable = GetComponent<Grabbable>();
    ResolveAudioSourcesIfNeeded();
    ResolveMixerSnapshotIfNeeded();
    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Awake");
}

void OnValidate()
{
    if (holdDuration < 0f) holdDuration = 0f;
    if (dropDisableDuration < 0f) dropDisableDuration = 0f;
    if (audioCrossfadeDuration < 0f) audioCrossfadeDuration = 0f;
    if (snapshotTransitionTime < 0f) snapshotTransitionTime = 0f;
}

void OnDisable()
{
    if (holdRoutine != null)
    {
        StopCoroutine(holdRoutine);
        holdRoutine = null;
    }
    holdingHands.Clear();
}

// Hook these in Grabbable's UnityEvents: On Grab / On Release
public void OnGrabFromAutoHand(Hand hand) { OnGrab(hand); }
public void OnReleaseFromAutoHand(Hand hand) { OnRelease(hand); }

void OnGrab(Hand hand)
{
    if (hand == null) return;
    holdingHands.Add(hand);
    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Grabbed by " + hand.name, this);

    if (onlyOnce && hasTeleported) return;

    if (holdRoutine == null)
        holdRoutine = StartCoroutine(HoldThenTeleport());
}

void OnRelease(Hand hand)
{
    if (hand != null) holdingHands.Remove(hand);
    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Released by " + (hand ? hand.name : "null"), this);

    if (holdingHands.Count == 0 && holdRoutine != null)
    {
        StopCoroutine(holdRoutine);
        holdRoutine = null;
        if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Hold canceled", this);
    }
}

IEnumerator HoldThenTeleport()
{
    float elapsed = 0f;

    while (elapsed < holdDuration)
    {
        if (holdingHands.Count == 0)
        {
            if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Hold interrupted", this);
            holdRoutine = null;
            yield break;
        }
        elapsed += Time.unscaledDeltaTime;
        yield return null;
    }

    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Hold complete, teleporting", this);
    Teleport();
    holdRoutine = null;
}

[ContextMenu("Teleport Now (Test)")]
public void Teleport()
{
    if (hasTeleported && onlyOnce) return;

    if (!teleportDestination)
    {
        Debug.LogWarning("[HoldToTeleport_Meta] No teleportDestination assigned.", this);
        return;
    }

    Camera cam = Camera.main;
    if (cam == null)
    {
        Debug.LogWarning("[HoldToTeleport_Meta] No Main Camera found. Tag your center-eye camera as MainCamera.", this);
        return;
    }

    if (rigRoot == null)
        rigRoot = ResolveRigRoot(cam.transform);

    if (rigRoot == null)
    {
        Debug.LogWarning("[HoldToTeleport_Meta] Could not resolve rig root. Assign rigRoot in the inspector.", this);
        return;
    }

    StartCoroutine(TeleportEndOfFrame(cam, rigRoot));
}

IEnumerator TeleportEndOfFrame(Camera cam, Transform root)
{
    // Let tracking update this frame before we move
    yield return new WaitForEndOfFrame();

    Transform head = cam.transform;

    // Disable CharacterController if present to avoid collision pushes
    CharacterController cc = root.GetComponent<CharacterController>();
    bool hadCC = cc && cc.enabled;
    if (hadCC) cc.enabled = false;

    // Compute desired yaw alignment (planar)
    float deltaYaw = 0f;
    if (alignYawToDestination)
    {
        Vector3 headF = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        Vector3 destF = Vector3.ProjectOnPlane(teleportDestination.forward, Vector3.up).normalized;
        if (headF.sqrMagnitude > 0.0001f && destF.sqrMagnitude > 0.0001f)
            deltaYaw = Vector3.SignedAngle(headF, destF, Vector3.up);
    }

    // Step 1: translate so the head lands exactly at the waypoint
    Vector3 offset = teleportDestination.position - head.position;
    root.position += offset;
    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Moved head to " + teleportDestination.position, this);

    // Step 2: rotate around the destination to match yaw without moving the head away
    if (alignYawToDestination && Mathf.Abs(deltaYaw) > 0.01f)
    {
        root.RotateAround(teleportDestination.position, Vector3.up, deltaYaw);
        if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Rotated yaw by " + deltaYaw + " deg", this);
    }

    if (hadCC) cc.enabled = true;

    hasTeleported = true;
    if (disableOnTeleport) disableOnTeleport.SetActive(false);

    // Switch mixer snapshot
    ChangeMixerSnapshot();

    // Audio crossfade after teleport
    StartCoroutine(CrossfadeAmbience());

    if (releaseAfterTeleport)
        StartCoroutine(ForceDropByColliderToggle());

    if (deactivateAfterTeleport)
        gameObject.SetActive(false);
}

IEnumerator ForceDropByColliderToggle()
{
    Collider[] cols = GetComponents<Collider>();
    for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
    if (grabbable) grabbable.enabled = false;

    yield return new WaitForSeconds(dropDisableDuration);

    if (grabbable) grabbable.enabled = true;
    for (int i = 0; i < cols.Length; i++) cols[i].enabled = true;

    holdingHands.Clear();
    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Forced drop complete", this);
}

Transform ResolveRigRoot(Transform head)
{
    if (rigRoot != null) return rigRoot;

    // Prefer a parent that has a CharacterController (common on OVRPlayerController)
    Transform t = head;
    while (t != null)
    {
        if (t.GetComponent<CharacterController>() != null)
            return t;
        t = t.parent;
    }

    // Fallback to the top of the head's hierarchy
    return head.root;
}

// Optional: reset for re-use if onlyOnce is true
public void ResetTeleportState()
{
    hasTeleported = false;
}

// Resolve audio sources by object name if not manually wired in the inspector
void ResolveAudioSourcesIfNeeded()
{
    if (eerieAmbiance == null && !string.IsNullOrEmpty(eerieAmbianceObjectName))
    {
        GameObject go = GameObject.Find(eerieAmbianceObjectName);
        if (go) eerieAmbiance = go.GetComponent<AudioSource>();
    }
    if (calmAmbiance == null && !string.IsNullOrEmpty(calmAmbianceObjectName))
    {
        GameObject go = GameObject.Find(calmAmbianceObjectName);
        if (go) calmAmbiance = go.GetComponent<AudioSource>();
    }
}

void ResolveMixerSnapshotIfNeeded()
{
    if (partyAfterSnapshot == null && mixer != null && !string.IsNullOrEmpty(partyAfterSnapshotName))
    {
        var snap = mixer.FindSnapshot(partyAfterSnapshotName);
        if (snap != null) partyAfterSnapshot = snap;
    }
}

void ChangeMixerSnapshot()
{
    AudioMixerSnapshot snap = partyAfterSnapshot;

    if (snap == null && mixer != null && !string.IsNullOrEmpty(partyAfterSnapshotName))
    {
        snap = mixer.FindSnapshot(partyAfterSnapshotName);
    }

    if (snap != null)
    {
        snap.TransitionTo(snapshotTransitionTime);
        if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Transitioned to mixer snapshot '" + snap.name + "' in " + snapshotTransitionTime + "s", this);
    }
    else if (debugLogs)
    {
        Debug.LogWarning("[HoldToTeleport_Meta] Mixer snapshot not found. Assign 'partyAfterSnapshot' or set 'mixer' and 'partyAfterSnapshotName'.", this);
    }
}

IEnumerator CrossfadeAmbience()
{
    // Nothing to do if both sources are missing
    if (eerieAmbiance == null && calmAmbiance == null)
        yield break;

    float duration = Mathf.Max(0.0001f, audioCrossfadeDuration);

    // Ensure calm ambience is playing when we start the fade (if requested)
    if (calmAmbiance != null && startCalmIfNotPlaying && !calmAmbiance.isPlaying)
        calmAmbiance.Play();

    float startEerie = eerieAmbiance != null ? eerieAmbiance.volume : 0f;
    float startCalm = calmAmbiance != null ? calmAmbiance.volume : 0f;

    float targetEerie = 0f;
    float targetCalm = 1f;

    float t = 0f;
    while (t < duration)
    {
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / duration);

        if (eerieAmbiance != null)
            eerieAmbiance.volume = Mathf.Lerp(startEerie, targetEerie, k);

        if (calmAmbiance != null)
            calmAmbiance.volume = Mathf.Lerp(startCalm, targetCalm, k);

        yield return null;
    }

    if (eerieAmbiance != null)
    {
        eerieAmbiance.volume = targetEerie;
        if (stopEerieWhenDone && targetEerie <= 0f)
            eerieAmbiance.Stop();
    }

    if (calmAmbiance != null)
        calmAmbiance.volume = targetCalm;

    if (debugLogs) Debug.Log("[HoldToTeleport_Meta] Audio crossfade complete", this);
}
}