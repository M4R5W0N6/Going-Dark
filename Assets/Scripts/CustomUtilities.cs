using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CustomUtilities
{
    public static readonly float DefaultScalarDistance = 100f;
    public static readonly float DefaultRaycastThreshold = 0.1f;

    public static Vector3 GetScreenPosition(Vector3 worldspace)
    {
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camPos = Camera.main.transform.position + camForward;
        float distInFrontOfCamera = Vector3.Dot(worldspace - camPos, camForward);
        if (distInFrontOfCamera < 0f)
        {
            worldspace -= camForward * distInFrontOfCamera;
        }

        return RectTransformUtility.WorldToScreenPoint(Camera.main, worldspace);
    }

    public static Vector3 GetFlattenedWorldPosition(Vector3 worldspace)
    {
        Vector3 screenPos = GetScreenPosition(worldspace);
        screenPos.z = DefaultScalarDistance;
        
        return Camera.main.ScreenToWorldPoint(screenPos);
    }

    public static void SetLayerRecursively(GameObject obj, string layerName)
    {
        if (null == obj)
        {
            return;
        }

        obj.layer = LayerMask.NameToLayer(layerName);

        foreach (Transform child in obj.transform)
        {
            if (null == child)
            {
                continue;
            }
            SetLayerRecursively(child.gameObject, layerName);
        }
    }
}
