using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CustomUtilities
{
    public static readonly float DefaultDistance = 100f;

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
}
