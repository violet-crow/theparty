using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class DrinkDetector : MonoBehaviour
{
[Header("References")]
public Transform head;      // Player head
public Transform mouthTip;  // Top of cup; forward points out of opening
public AudioSource audioSource;
[Header("Audio")]
public AudioClip drinkClip;
[Range(0f, 1f)] public float drinkVolume = 1f;

[Header("Gesture thresholds")]
[Tooltip("Max distance from cup top to head (meters).")]
public float proximityThreshold = 0.15f;
[Tooltip("Signed tilt range (deg): 0 = vertical, -90 = horizontal toward player.")]
public float minTiltDeg = -120f;
public float maxTiltDeg = -20f;

[Header("Dwell and cooldown")]
public float dwellSeconds = 0.35f;
public float cooldownSeconds = 1.0f;

[Header("Events")]
public UnityEvent OnDrink;

// Internal
float _dwellTimer;
float _cooldownUntil;
float _proximitySqr;

void Awake()
{
    _proximitySqr = proximityThreshold * proximityThreshold;
    if (audioSource == null) audioSource = GetComponent<AudioSource>();
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
    if (Time.time < _cooldownUntil) return;

    // 1) Cup top near head
    bool proximityOK = (mouthTip.position - head.position).sqrMagnitude <= _proximitySqr;

    // 2) Tilt toward player within [-120°, -20°]
    float tiltMag = Vector3.Angle(transform.up, Vector3.up); // 0 = vertical
    Vector3 toHead = (head.position - mouthTip.position).normalized;
    bool towardsHead = Vector3.Dot(mouthTip.forward, toHead) > 0f;
    float signedTilt = towardsHead ? -tiltMag : tiltMag;
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
}

void TriggerDrink()
{
    if (audioSource != null && drinkClip != null)
        audioSource.PlayOneShot(drinkClip, drinkVolume);

    OnDrink?.Invoke();
}
}