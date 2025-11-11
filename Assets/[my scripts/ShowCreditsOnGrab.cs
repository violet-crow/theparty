using UnityEngine;
using Autohand;

public class ShowCreditsOnGrab : MonoBehaviour
{
    public GameObject creditsScreenPrefab;
    public float distanceFromHead = 0.5f;   // 0.4–0.7m usually fills view
    public bool oneShot = true;
    public bool destroyObjectAfter = false;
    bool shown;

    public void OnGrabFromAutoHand(Hand hand)
    {
        if (oneShot && shown) return;

        Camera cam = Camera.main;
        if (!cam || creditsScreenPrefab == null) { Debug.LogWarning("[ShowCreditsOnGrab] Missing camera or prefab."); return; }

        GameObject screen = Instantiate(creditsScreenPrefab);
        Transform t = screen.transform;
        t.SetParent(cam.transform, false);
        t.localPosition = new Vector3(0f, 0f, distanceFromHead);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        shown = true;
        if (destroyObjectAfter) Destroy(gameObject);
    }
}