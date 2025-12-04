using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class IntoxicationManager : MonoBehaviour
{
public static IntoxicationManager Instance { get; private set; }
[Header("URP Volume (Global)")]
public Volume globalVolume;

[Header("Progress per drink")]
[Range(0f, 1f)] public float incrementPercentPerDrink = 0.2f;
public int blackoutDrinkCount = 5;

[Header("Max color deltas at 100% progress")]
public float maxSaturationDelta = 40f;
public float maxContrastDelta = 25f;
public float colorBlendSeconds = 0.35f;

[Header("Motion Blur (VR caution)")]
public bool enableMotionBlur = true;
[Range(0f, 1f)] public float maxMotionBlurIntensity = 0.5f;
[Range(0f, 1f)] public float maxMotionBlurClamp = 0.25f;
public MotionBlurQuality motionBlurQuality = MotionBlurQuality.Medium;

[Header("Blackout UI")]
public CanvasGroup blackoutCanvasGroup;
public float blackoutFadeSeconds = 0.8f;

[Header("Restart behavior")]
public bool reloadSceneOnRestart = false;
public string restartSceneName = "";

[Header("Blackout controls")]
[Tooltip("Hold any face button for this duration to restart.")]
public float holdToRestartSeconds = 2f;
[Tooltip("Listen for face buttons during blackout.")]
public bool listenForFaceButtons = true;

[Header("Optional control during blackout")]
public Behaviour[] disableOnBlackout;

// Internal
int drinkCount = 0;
ColorAdjustments colorAdj;
MotionBlur motionBlur;
float baseSaturation = 0f, baseContrast = 0f;
Coroutine colorRoutine;
Coroutine blackoutRoutine;
bool isBlackoutActive = false;
float holdTimer = 0f;

void Awake()
{
    if (Instance != null && Instance != this) { Destroy(gameObject); return; }
    Instance = this;
}

void Start()
{
    if (globalVolume == null || globalVolume.profile == null)
    {
        Debug.LogError("IntoxicationManager: Assign a Global Volume with a valid Profile.");
        return;
    }

    if (!globalVolume.profile.TryGet(out colorAdj))
        colorAdj = globalVolume.profile.Add<ColorAdjustments>();
    if (!globalVolume.profile.TryGet(out motionBlur))
        motionBlur = globalVolume.profile.Add<MotionBlur>();

    baseSaturation = colorAdj.saturation.value;
    baseContrast = colorAdj.contrast.value;

    colorAdj.active = true;
    colorAdj.saturation.overrideState = true;
    colorAdj.contrast.overrideState = true;

    motionBlur.active = false;
    motionBlur.intensity.overrideState = true;
    motionBlur.clamp.overrideState = true;
    motionBlur.quality.value = motionBlurQuality;

    if (blackoutCanvasGroup)
    {
        blackoutCanvasGroup.alpha = 0f;
        blackoutCanvasGroup.interactable = false;
        blackoutCanvasGroup.blocksRaycasts = false;
    }

    ApplyProgressEffectsImmediate();
}

void Update()
{
    if (!isBlackoutActive || !listenForFaceButtons) return;

    if (IsAnyFaceButtonHeld())
    {
        holdTimer += Time.deltaTime;
        if (holdTimer >= holdToRestartSeconds)
        {
            holdTimer = 0f;
            isBlackoutActive = false; // prevent double-trigger
            RestartExperience();
        }
    }
    else
    {
        holdTimer = 0f;
    }
}

// Hook this to DrinkDetector.OnDrink
public void HandleDrink()
{
    if (globalVolume == null || globalVolume.profile == null) return;

    drinkCount++;
    ApplyProgressEffectsBlended();

    if (drinkCount >= blackoutDrinkCount)
        ApplyBlackout();
}

void ApplyProgressEffectsBlended()
{
    float progress = Mathf.Clamp01(drinkCount * incrementPercentPerDrink);

    float targetSat = baseSaturation + maxSaturationDelta * progress;
    float targetCon = baseContrast + maxContrastDelta * progress;
    StartColorBlend(targetSat, targetCon, colorBlendSeconds);

    if (enableMotionBlur && motionBlur != null)
    {
        motionBlur.active = progress > 0f;
        motionBlur.intensity.value = maxMotionBlurIntensity * progress;
        motionBlur.clamp.value = maxMotionBlurClamp * progress;
    }
}

void ApplyProgressEffectsImmediate()
{
    float progress = Mathf.Clamp01(drinkCount * incrementPercentPerDrink);

    colorAdj.saturation.value = baseSaturation + maxSaturationDelta * progress;
    colorAdj.contrast.value = baseContrast + maxContrastDelta * progress;

    if (enableMotionBlur && motionBlur != null)
    {
        motionBlur.active = progress > 0f;
        motionBlur.intensity.value = maxMotionBlurIntensity * progress;
        motionBlur.clamp.value = maxMotionBlurClamp * progress;
    }
    else if (motionBlur != null)
    {
        motionBlur.active = false;
    }
}

void StartColorBlend(float targetSaturation, float targetContrast, float duration)
{
    if (colorRoutine != null) StopCoroutine(colorRoutine);
    colorRoutine = StartCoroutine(ColorBlendRoutine(targetSaturation, targetContrast, duration));
}

IEnumerator ColorBlendRoutine(float targetSaturation, float targetContrast, float duration)
{
    float startSat = colorAdj.saturation.value;
    float startCon = colorAdj.contrast.value;
    float t = 0f;
    duration = Mathf.Max(0.01f, duration);

    while (t < 1f)
    {
        t += Time.deltaTime / duration;
        float tt = Mathf.Clamp01(t);
        colorAdj.saturation.value = Mathf.Lerp(startSat, targetSaturation, tt);
        colorAdj.contrast.value = Mathf.Lerp(startCon, targetContrast, tt);
        yield return null;
    }

    colorAdj.saturation.value = targetSaturation;
    colorAdj.contrast.value = targetContrast;
    colorRoutine = null;
}

void ApplyBlackout()
{
    if (blackoutCanvasGroup == null)
    {
        Debug.LogWarning("IntoxicationManager: No blackout UI assigned; performing soft reset.");
        RestartExperience();
        return;
    }

    if (disableOnBlackout != null)
    {
        for (int i = 0; i < disableOnBlackout.Length; i++)
            if (disableOnBlackout[i]) disableOnBlackout[i].enabled = false;
    }
    DisableBlackoutObjects();

    if (blackoutRoutine != null) StopCoroutine(blackoutRoutine);
    blackoutRoutine = StartCoroutine(BlackoutSequence());
}

IEnumerator BlackoutSequence()
{
    float t = 0f;
    float dur = Mathf.Max(0.01f, blackoutFadeSeconds);
    while (t < 1f)
    {
        t += Time.deltaTime / dur;
        blackoutCanvasGroup.alpha = Mathf.Clamp01(t);
        yield return null;
    }

    blackoutCanvasGroup.alpha = 1f;
    blackoutCanvasGroup.interactable = true;
    blackoutCanvasGroup.blocksRaycasts = true;
    isBlackoutActive = true;
    holdTimer = 0f;
    blackoutRoutine = null;
}

public void RestartExperience()
{
    if (reloadSceneOnRestart)
    {
        string scene = string.IsNullOrEmpty(restartSceneName) ? SceneManager.GetActiveScene().name : restartSceneName;
        SceneManager.LoadScene(scene);
        return;
    }

    drinkCount = 0;

    if (colorRoutine != null) { StopCoroutine(colorRoutine); colorRoutine = null; }
    if (blackoutRoutine != null) { StopCoroutine(blackoutRoutine); blackoutRoutine = null; }

    if (blackoutCanvasGroup)
    {
        blackoutCanvasGroup.alpha = 0f;
        blackoutCanvasGroup.interactable = false;
        blackoutCanvasGroup.blocksRaycasts = false;
    }

    isBlackoutActive = false;
    holdTimer = 0f;

    ApplyProgressEffectsImmediate();

    if (disableOnBlackout != null)
    {
        for (int i = 0; i < disableOnBlackout.Length; i++)
            if (disableOnBlackout[i]) disableOnBlackout[i].enabled = true;
    }
    RestoreBlackoutObjects();
}

bool IsAnyFaceButtonHeld()
{
// Quest 3: A/B (RTouch) and X/Y (LTouch)
return OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch) || // A
OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch) || // B
OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LTouch) || // X
OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch);   // Y
}


// blackout disable
// Field (Inspector list)
[Header("Blackout: Objects to disable")]
[Tooltip("GameObjects to SetActive(false) during blackout; restored on restart.")]
public GameObject[] disableObjectsOnBlackout;

// Internal state
bool[] _blackoutObjActiveStates;

// Methods
void DisableBlackoutObjects()
{
if (disableObjectsOnBlackout == null) return;
if (_blackoutObjActiveStates == null || _blackoutObjActiveStates.Length != disableObjectsOnBlackout.Length)
    _blackoutObjActiveStates = new bool[disableObjectsOnBlackout.Length];

for (int i = 0; i < disableObjectsOnBlackout.Length; i++)
{
    var go = disableObjectsOnBlackout[i];
    if (!go) continue;
    _blackoutObjActiveStates[i] = go.activeSelf; // remember prior state
    go.SetActive(false);
}
}
void RestoreBlackoutObjects()
{
if (disableObjectsOnBlackout == null || _blackoutObjActiveStates == null) return;
for (int i = 0; i < disableObjectsOnBlackout.Length; i++)
{
    var go = disableObjectsOnBlackout[i];
    if (!go) continue;
    go.SetActive(_blackoutObjActiveStates[i]); // restore prior state
}
}
}