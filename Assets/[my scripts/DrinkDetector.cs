using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using TMPro;

[DisallowMultipleComponent]
public class DrinkDetector : MonoBehaviour
{
[Header("References")]
public Transform head;       // Player head
public Transform mouthTip;   // Top of cup; axis points out of opening (use up/forward/right accordingly)
public AudioSource audioSource;
[Header("Audio")]
public AudioClip drinkClip;
[Range(0f, 1f)] public float drinkVolume = 1f;

[Header("Gesture thresholds")]
[Tooltip("Max distance from cup top to head (meters).")]
public float proximityThreshold = 0.15f;
[Tooltip("Signed tilt range (deg): 0 = mouth up, -90 = horizontal toward player.")]
public float minTiltDeg = -120f;
public float maxTiltDeg = -50f;

[Header("Dwell and cooldown")]
public float dwellSeconds = 0.35f;
public float cooldownSeconds = 1.0f;

[Header("Events")]
public UnityEvent OnDrink;

[Header("Debug UI")]
[Tooltip("Optional explicit drinking point (e.g., mouth transform). If null, uses head.position.")]
public Transform drinkingPoint;
[Tooltip("World-space TMP text to show tilt and distance.")]
public TextMeshProUGUI debugText;
[Tooltip("Anchor for the debug text (defaults to head).")]
public Transform debugAnchor;
[Tooltip("Local offset from the anchor for placing the text.")]
public Vector3 debugLocalOffset = new Vector3(0f, -0.05f, 0.4f);

// Internal
float _dwellTimer;
float _cooldownUntil;
float _proximitySqr;
Coroutine _vibrationRoutine;

void Awake()
{
    _proximitySqr = proximityThreshold * proximityThreshold;
    if (audioSource == null) audioSource = GetComponent<AudioSource>();
    if (debugAnchor == null) debugAnchor = head;
}

void OnValidate()
{
    proximityThreshold = Mathf.Max(0f, proximityThreshold);
    _proximitySqr = proximityThreshold * proximityThreshold;
    if (minTiltDeg > maxTiltDeg) { float t = minTiltDeg; minTiltDeg = maxTiltDeg; maxTiltDeg = t; }
    dwellSeconds = Mathf.Max(0.01f, dwellSeconds);
    cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
}

void Update()
{
    if (head == null || mouthTip == null) return;

    Vector3 drinkPointPos = drinkingPoint ? drinkingPoint.position : head.position;

    if (Time.time < _cooldownUntil)
    {
        UpdateDebug(drinkPointPos);
        return;
    }

    // 1) Cup top near head/drink point
    float distSqr = (mouthTip.position - drinkPointPos).sqrMagnitude;
    bool proximityOK = distSqr <= _proximitySqr;

    // 2) Tilt toward player within [-120°, -50°]
    Vector3 mouthNormal = mouthTip.up; // change to .forward or .right if that matches your cup opening
    float tiltMag = Vector3.Angle(mouthNormal, Vector3.up); // 0 = mouth up, 180 = mouth down
    Vector3 toTarget = (drinkPointPos - mouthTip.position).normalized;
    bool towardsPlayer = Vector3.Dot(mouthNormal, toTarget) > 0f;
    float signedTilt = towardsPlayer ? -tiltMag : tiltMag;
    bool tiltOK = signedTilt >= minTiltDeg && signedTilt <= maxTiltDeg;

    if (proximityOK && tiltOK)
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

    UpdateDebug(drinkPointPos, signedTilt, Mathf.Sqrt(distSqr));
}

void TriggerDrink()
{
    if (audioSource != null && drinkClip != null)
        audioSource.PlayOneShot(drinkClip, drinkVolume);

    // Vibrate right Quest controller for 2 seconds
    if (_vibrationRoutine != null) { StopCoroutine(_vibrationRoutine); _vibrationRoutine = null; }
    _vibrationRoutine = StartCoroutine(VibrateRightController(1.5f));

    OnDrink?.Invoke();
}

void UpdateDebug(Vector3 drinkPointPos, float tiltDeg = 0f, float dist = 0f)
{
    if (debugText == null || debugAnchor == null) return;

    debugText.transform.position = debugAnchor.TransformPoint(debugLocalOffset);
    debugText.transform.rotation = Quaternion.LookRotation(debugAnchor.forward, debugAnchor.up);

    if (dist == 0f) dist = Vector3.Distance(mouthTip.position, drinkPointPos);
    if (Mathf.Approximately(tiltDeg, 0f))
    {
        Vector3 mouthNormal = mouthTip.up;
        float tiltMag = Vector3.Angle(mouthNormal, Vector3.up);
        Vector3 toTarget = (drinkPointPos - mouthTip.position).normalized;
        bool towardsPlayer = Vector3.Dot(mouthNormal, toTarget) > 0f;
        tiltDeg = towardsPlayer ? -tiltMag : tiltMag;
    }

    debugText.text = $"Tilt: {tiltDeg:F0}°  Dist: {dist:F2}m";
}

// Quest 3 haptics (requires Oculus Integration)
IEnumerator VibrateRightController(float seconds, float frequency = 0.8f, float amplitude = 1f)
{
    float elapsed = 0f;
    while (elapsed < seconds)
    {
        OVRInput.SetControllerVibration(frequency, amplitude, OVRInput.Controller.RTouch);
        elapsed += Time.unscaledDeltaTime;
        yield return null;
    }
    OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
}
}