
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    [Header("Tracer Settings")]
    public float speed = 180f;
    public float lifeTime = 2f;

    private LineRenderer line;
    private Vector3 startPosition;
    private Vector3 endPosition;

    public void Initialize(
        Vector3 start,
        Vector3 end,
        Color bulletColor,
        float bulletWidth,
        float bulletSpeed)
    {
        line = GetComponent<LineRenderer>();

        startPosition = start;
        endPosition = end;

        speed = bulletSpeed;

        line.positionCount = 2;
        line.startWidth = bulletWidth;
        line.endWidth = bulletWidth * 0.15f;

        line.startColor = bulletColor;
        line.endColor = bulletColor;

        line.useWorldSpace = true;

        StartCoroutine(Fly());
    }

    private IEnumerator Fly()
    {
        float distance = Vector3.Distance(
            startPosition,
            endPosition
        );

        float duration = distance / Mathf.Max(speed, 0.01f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);

            Vector3 currentPosition = Vector3.Lerp(
                startPosition,
                endPosition,
                t
            );

            line.SetPosition(0, currentPosition);
            line.SetPosition(1, endPosition);

            yield return null;
        }

        Destroy(gameObject, lifeTime);
    }
}
