using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class DrinkDetector : MonoBehaviour
{
[Header("References")]
[Tooltip("Head transform (OVRCameraRig CenterEyeAnchor).")]
public Transform head;
[Tooltip("Child transform at the bottle mouth/lip. Forward should point out of the opening.")]
public Transform mouthTip;
[Tooltip("AudioSource to play the drink sound from (bottle or head).")]
public AudioSource audioSource;
[Header("Audio")]
[Tooltip("Clip to play when drinking is detected.")]
public AudioClip drinkClip;
[Range(0f, 1f)] public float drinkVolume = 1f;

[Header("Mouth position offsets (relative to head)")]
[Tooltip("Forward offset from head (meters) to approximate mouth position.")]
public float mouthForwardOffset = 0.08f;
[Tooltip("Downward offset from head (meters) to approximate mouth position.")]
public float mouthDownOffset = 0.06f;

[Header("Gesture thresholds")]
[Tooltip("Max distance from MouthTip to mouth position (meters).")]
public float proximityThreshold = 0.15f;
[Tooltip("Alignment of MouthTip forward toward head (0..1).")]
[Range(0f, 1f)] public float aimDotThreshold = 0.7f;
[Tooltip("Bottle tilt: dot(transform.up, world up) must be <= this value (e.g., -0.6 for ~>127° tilt).")]
[Range(-1f, 1f)] public float pourUpDotThreshold = -0.6f;

[Header("Dwell and cooldown")]
[Tooltip("Time the gesture must be held to count as a drink (seconds).")]
public float dwellSeconds = 0.35f;
[Tooltip("Cooldown after a drink to prevent rapid retriggers (seconds).")]
public float cooldownSeconds = 1.0f;

[Header("Motion stability")]
[Tooltip("Require low angular velocity during dwell to avoid false triggers.")]
public bool requireLowAngularVelocity = true;
[Tooltip("Maximum angular velocity allowed during dwell (rad/s).")]
public float maxAngularVelocity = 2.0f;
[Tooltip("Use Oculus controller angular velocity for RTouch when available.")]
public bool useOVRControllerVelocity = true;

[Header("Events")]
public UnityEvent OnDrink;

[Header("Debug")]
public bool debugDraw = false;

// Internal
float _dwellTimer = 0f;
float _cooldownUntil = 0f;
Quaternion _lastRotation;
float _lastAngularVelRad = 0f;
float _proximitySqr;
Transform _t;

void Awake()
{
    _t = transform;
    _lastRotation = _t.rotation;
    _proximitySqr = proximityThreshold * proximityThreshold;
    if (audioSource == null) audioSource = GetComponent<AudioSource>();
}

void OnValidate()
{
    if (proximityThreshold < 0f) proximityThreshold = 0f;
    _proximitySqr = proximityThreshold * proximityThreshold;
    dwellSeconds = Mathf.Max(0.01f, dwellSeconds);
    cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
    maxAngularVelocity = Mathf.Max(0f, maxAngularVelocity);
}

void Update()
{
    if (head == null || mouthTip == null) return;

    // Cooldown
    if (Time.time < _cooldownUntil)
    {
        // Update angular velocity for smoother resume
        UpdateAngularVelocity();
        return;
    }

    // Compute mouth position in head space
    Vector3 mouthPos = head.position + head.forward * mouthForwardOffset - head.up * mouthDownOffset;

    // Proximity
    Vector3 tipPos = mouthTip.position;
    float distSqr = (tipPos - mouthPos).sqrMagnitude;
    bool proximityOK = distSqr <= _proximitySqr;

    // Aim/alignment
    Vector3 toHead = (head.position - tipPos);
    float toHeadLen = toHead.magnitude;
    float aimDot = 0f;
    if (toHeadLen > 1e-5f)
    {
        toHead /= toHeadLen;
        aimDot = Vector3.Dot(mouthTip.forward, toHead);
    }
    bool aimOK = aimDot >= aimDotThreshold;

    // Pour tilt
    float upDot = Vector3.Dot(_t.up, Vector3.up);
    bool pourOK = upDot <= pourUpDotThreshold;

    // Angular velocity
    UpdateAngularVelocity();
    bool stableOK = !requireLowAngularVelocity || (_lastAngularVelRad <= maxAngularVelocity);

    // Dwell logic
    if (proximityOK && aimOK && pourOK && stableOK)
    {
        _dwellTimer += Time.deltaTime;
        if (_dwellTimer >= dwellSeconds)
        {
            TriggerDrink();
            _dwellTimer = 0f;
            _cooldownUntil = Time.time + cooldownSeconds;
        }
    }
    else
    {
        _dwellTimer = 0f;
    }

    if (debugDraw)
    {
        DebugDraw(mouthPos, tipPos, distSqr, aimDot, upDot, _lastAngularVelRad, proximityOK, aimOK, pourOK, stableOK);
    }
}

void TriggerDrink()
{
    // Play audio
    if (audioSource != null && drinkClip != null)
    {
        audioSource.PlayOneShot(drinkClip, drinkVolume);
    }

    // Event
    OnDrink?.Invoke();
}

void UpdateAngularVelocity()
{
    // Prefer controller angular velocity if available
    if (useOVRControllerVelocity)
    {
        #if OVRPLUGIN_PRESENT
// Right controller by default
var vel = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
_lastAngularVelRad = vel.magnitude;
if (_lastAngularVelRad > 0f) { _lastRotation = _t.rotation; return; }
#endif
}
// Fallback: estimate from transform rotation
    float angleDeg = Quaternion.Angle(_lastRotation, _t.rotation);
    _lastAngularVelRad = (angleDeg * Mathf.Deg2Rad) / Mathf.Max(Time.deltaTime, 1e-5f);
    _lastRotation = _t.rotation;
}

void DebugDraw(Vector3 mouthPos, Vector3 tipPos, float distSqr, float aimDot, float upDot, float angVel, bool proxOK, bool aimOK, bool pourOK, bool stableOK)
{
    // Mouth position
    Color mouthColor = proxOK ? Color.green : Color.yellow;
    Debug.DrawLine(head.position, mouthPos, mouthColor);
    // Tip forward direction
    Debug.DrawRay(tipPos, mouthTip.forward * 0.1f, aimOK ? Color.green : Color.red);
    // To head direction
    Debug.DrawRay(tipPos, (head.position - tipPos).normalized * 0.1f, aimOK ? Color.green : Color.red);
    // Bottle up vs world up
    Debug.DrawRay(_t.position, _t.up * 0.15f, pourOK ? Color.green : Color.red);

    // Optional gizmo sphere (only visible in Scene view if gizmos on)
    #if UNITY_EDITOR
UnityEditor.Handles.color = mouthColor;
UnityEditor.Handles.SphereHandleCap(0, mouthPos, Quaternion.identity, 0.02f, EventType.Repaint);
#endif
// Compact stats
    string stats = $"DrinkDetector: dist={Mathf.Sqrt(distSqr):F3}m aimDot={aimDot:F2} upDot={upDot:F2} angVel={angVel:F2} | OK: prox={proxOK} aim={aimOK} pour={pourOK} stable={stableOK} dwell={_dwellTimer:F2}/{dwellSeconds:F2}";
    Debug.Log(stats);
}
}