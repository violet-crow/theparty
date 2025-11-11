using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Autohand;

[RequireComponent(typeof(Grabbable))]
[DisallowMultipleComponent]
public class HoldToTeleport_AutoHand_OVR : MonoBehaviour
{
    [Header("Teleport")]
    [Tooltip("Where the player's head (center eye) should end up.")]
    public Transform teleportDestination;
    [Tooltip("Your OVRCameraRig (auto-found if left empty).")]
    public OVRCameraRig rig;

    [Header("Timing")]
    [Tooltip("Seconds the object must be held continuously to trigger teleport.")]
    public float holdDuration = 2f;

    [Header("Behavior")]
    [Tooltip("Allow only a single teleport from this object.")]
    public bool onlyOnce = true;

    [Tooltip("Release the object from the hand(s) after teleport by briefly disabling colliders.")]
    public bool releaseAfterTeleport = true;

    [Tooltip("Disable the object after teleport.")]
    public bool deactivateAfterTeleport = false;

    [Tooltip("How long to disable colliders to force a drop.")]
    public float dropDisableDuration = 0.05f;

    Grabbable grabbable;
    readonly HashSet<Hand> holdingHands = new HashSet<Hand>();
    Coroutine holdRoutine;
    bool hasTeleported;

    void Awake()
    {
        grabbable = GetComponent<Grabbable>();
        if (!rig) rig = FindAnyObjectByType<OVRCameraRig>();

        // Subscribe to AutoHand C# events (not UnityEvents).
        // The event delegate signature varies by version, so we provide overloads below.
        grabbable.OnGrabEvent += OnGrabEventHandler;
        grabbable.OnReleaseEvent += OnReleaseEventHandler;
    }

    void OnDestroy()
    {
        // Unsubscribe safely
        if (grabbable != null)
        {
            grabbable.OnGrabEvent -= OnGrabEventHandler;
            grabbable.OnReleaseEvent -= OnReleaseEventHandler;
        }
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

    // Event adapters (support common AutoHand signatures)

    // Some versions: event HandGrabEvent(Hand hand)
    void OnGrabEventHandler(Hand hand) { OnGrab(hand); }
    void OnReleaseEventHandler(Hand hand) { OnRelease(hand); }

    // Other versions: event HandGrabEvent(Hand hand, Grabbable grabbed)
    void OnGrabEventHandler(Hand hand, Grabbable _) { OnGrab(hand); }
    void OnReleaseEventHandler(Hand hand, Grabbable _) { OnRelease(hand); }

    // Core logic

    void OnGrab(Hand hand)
    {
        if (hand == null) return;
        holdingHands.Add(hand);

        if (onlyOnce && hasTeleported) return;

        if (holdRoutine == null)
            holdRoutine = StartCoroutine(HoldThenTeleport());
    }

    void OnRelease(Hand hand)
    {
        if (hand != null) holdingHands.Remove(hand);

        if (holdingHands.Count == 0 && holdRoutine != null)
        {
            StopCoroutine(holdRoutine);
            holdRoutine = null;
        }
    }

    IEnumerator HoldThenTeleport()
    {
        float t = 0f;

        while (t < holdDuration)
        {
            if (holdingHands.Count == 0)
            {
                holdRoutine = null;
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        Teleport();
        holdRoutine = null;
    }

    void Teleport()
    {
        if (hasTeleported && onlyOnce) return;

        if (!teleportDestination)
        {
            Debug.LogWarning("HoldToTeleport: No teleportDestination assigned.");
            return;
        }
        if (!rig)
        {
            Debug.LogWarning("HoldToTeleport: No OVRCameraRig found/assigned.");
            return;
        }

        // Align yaw to destination
        Transform head = rig.centerEyeAnchor;
        float headYaw = head.rotation.eulerAngles.y;
        float destYaw = teleportDestination.eulerAngles.y;
        float deltaYaw = Mathf.DeltaAngle(headYaw, destYaw);
        rig.transform.Rotate(0f, deltaYaw, 0f, Space.World);

        // Move rig so the head ends exactly at the destination
        Vector3 deltaPos = teleportDestination.position - head.position;
        rig.transform.position += deltaPos;

        hasTeleported = true;

        if (releaseAfterTeleport)
            StartCoroutine(ForceDropByColliderToggle());

        if (deactivateAfterTeleport)
            gameObject.SetActive(false);
    }

    IEnumerator ForceDropByColliderToggle()
    {
        // Briefly disable colliders + grabbable to force AutoHand to release
        var cols = GetComponents<Collider>();
        foreach (var c in cols) c.enabled = false;
        grabbable.enabled = false;

        yield return new WaitForSeconds(dropDisableDuration);

        grabbable.enabled = true;
        foreach (var c in cols) c.enabled = true;

        holdingHands.Clear();
    }

    // Optional: public API to reset one-time state
    public void ResetTeleportState()
    {
        hasTeleported = false;
    }
}