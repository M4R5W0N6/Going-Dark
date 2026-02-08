using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIToWorldspaceElement : MonoBehaviour
{
    [SerializeField]
    private RectTransform target;

    [SerializeField]
    private float moveSpeed = 2f;
    private Vector3 trackedPosition;

    private void FixedUpdate()
    {
        Vector3 screenPos = target.position;
        screenPos.z = CustomUtilities.DefaultScalarDistance;

        Camera camera = CustomUtilities.GetBestCamera(transform);
        if (camera == null)
            return;

        trackedPosition = camera.ScreenToWorldPoint(screenPos);

        transform.position = Vector3.Lerp(transform.position, trackedPosition, Time.fixedDeltaTime * moveSpeed);
    }
}
