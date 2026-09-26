using UnityEngine;

/// <summary>
/// Simple chase camera: sits behind and above the target, smoothly follows it,
/// and turns with the helicopter's heading (but ignores its tilt so you don't get sick).
/// </summary>
public class HeliFollowCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 14f;
    public float height = 5f;
    public float followSmoothing = 4f;
    public float lookAheadHeight = 1f;

    void LateUpdate()
    {
        if (!target) return;

        Vector3 flatForward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;

        Vector3 desiredPos = target.position - flatForward * distance + Vector3.up * height;
        float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPos, t);
        transform.LookAt(target.position + Vector3.up * lookAheadHeight);
    }
}
