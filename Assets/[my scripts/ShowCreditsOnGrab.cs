using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class ShowCreditsOnTrigger : MonoBehaviour
{
[Tooltip("Trigger box collider inside the recycling bin that activates credits.")]
public Collider recycleBinTrigger;
public GameObject creditsScreenPrefab;
public float distanceFromHead = 0.5f;
public float fadeDuration = 1.5f;
public bool useUnscaledTime = true;
public bool oneShot = true;
public bool destroyObjectAfter = false;

bool shown;

void OnTriggerEnter(Collider other)
{
    if (recycleBinTrigger == null) return;
    if (other != recycleBinTrigger) return;

    ShowCredits();
}

void ShowCredits()
{
    if (oneShot && shown) return;

    Camera cam = Camera.main;
    if (!cam || creditsScreenPrefab == null)
    {
        Debug.LogWarning("[ShowCreditsOnTrigger] Missing camera or prefab.");
        return;
    }

    GameObject screen = Instantiate(creditsScreenPrefab);
    Transform t = screen.transform;
    t.SetParent(cam.transform, false);
    t.localPosition = new Vector3(0f, 0f, distanceFromHead);
    t.localRotation = Quaternion.identity;
    t.localScale = Vector3.one;

    var group = screen.GetComponent<CanvasGroup>();
    if (group == null) group = screen.AddComponent<CanvasGroup>();
    group.alpha = 0f;
    group.blocksRaycasts = true;
    group.interactable = true;

    StartCoroutine(FadeCanvasGroup(group, 0f, 1f, fadeDuration));

    shown = true;
    if (destroyObjectAfter) Destroy(gameObject);
}

IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
{
    float t = 0f;
    group.alpha = from;

    while (t < duration)
    {
        t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float p = Mathf.Clamp01(t / duration);
        group.alpha = Mathf.Lerp(from, to, p);
        yield return null;
    }

    group.alpha = to;
}
}