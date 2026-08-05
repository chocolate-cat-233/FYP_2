using UnityEngine;

[DisallowMultipleComponent]
public sealed class Hide : MonoBehaviour
{
    [Header("Floating")]
    [Min(0f)]
    [SerializeField] private float floatHeight = 0.25f;
    [Min(0f)]
    [SerializeField] private float floatSpeed = 1f;

    [Header("Rotation")]
    [SerializeField] private Vector3 rotationSpeed =
        new Vector3(0f, 90f, 0f);

    private Vector3 baseLocalPosition;
    private float elapsedTime;
    private float appliedFloatOffset;

    private void OnEnable()
    {
        baseLocalPosition = transform.localPosition;
        elapsedTime = 0f;
        appliedFloatOffset = 0f;
    }

    private void OnDisable()
    {
        transform.localPosition = baseLocalPosition;
        appliedFloatOffset = 0f;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        // Preserve position changes made by other systems while this item floats.
        baseLocalPosition =
            transform.localPosition - Vector3.up * appliedFloatOffset;

        elapsedTime += deltaTime;
        appliedFloatOffset =
            Mathf.Sin(elapsedTime * floatSpeed * Mathf.PI * 2f) *
            floatHeight;

        transform.localPosition =
            baseLocalPosition + Vector3.up * appliedFloatOffset;
        transform.Rotate(rotationSpeed * deltaTime, Space.Self);
    }

    public void WhenBeTouch()
    {
        gameObject.SetActive(false);
    }
}
