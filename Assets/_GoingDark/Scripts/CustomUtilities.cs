using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CustomUtilities
{
    public static readonly float DefaultScalarDistance = 100f;
    public static readonly float DefaultRaycastThreshold = 0.1f;

    public static Vector3 GetScreenPosition(Vector3 worldspace)
    {
        Camera camera = GetBestCamera(worldspace);
        if (camera == null)
            return Vector3.zero;

        Vector3 camForward = camera.transform.forward;
        Vector3 camPos = camera.transform.position + camForward;
        float distInFrontOfCamera = Vector3.Dot(worldspace - camPos, camForward);
        if (distInFrontOfCamera < 0f)
        {
            worldspace -= camForward * distInFrontOfCamera;
        }

        return RectTransformUtility.WorldToScreenPoint(camera, worldspace);
    }

    public static Vector3 GetFlattenedWorldPosition(Vector3 worldspace)
    {
        Camera camera = GetBestCamera(worldspace);
        if (camera == null)
            return worldspace;

        Vector3 screenPos = GetScreenPosition(worldspace);
        screenPos.z = DefaultScalarDistance;
        
        return camera.ScreenToWorldPoint(screenPos);
    }

    public static Camera GetBestCamera(Transform referenceTransform)
    {
        if (referenceTransform == null)
            return GetBestCamera(Vector3.zero);

        return GetBestCamera(referenceTransform.position, referenceTransform.gameObject.scene.handle);
    }

    public static Camera GetBestCamera(Vector3 referencePosition)
    {
        return GetBestCamera(referencePosition, 0);
    }

    private static Camera GetBestCamera(Vector3 referencePosition, int preferredSceneHandle)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null &&
            mainCamera.isActiveAndEnabled &&
            mainCamera.gameObject.activeInHierarchy &&
            (preferredSceneHandle == 0 || mainCamera.gameObject.scene.handle == preferredSceneHandle))
        {
            return mainCamera;
        }

        Camera[] cameras = Camera.allCameras;
        if (cameras == null || cameras.Length == 0)
            return null;

        float bestScore = float.MaxValue;
        Camera bestCamera = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera candidate = cameras[i];
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                continue;

            float scenePenalty = 0f;
            if (preferredSceneHandle != 0 && candidate.gameObject.scene.handle != preferredSceneHandle)
                scenePenalty = 1000000f;

            float score = scenePenalty + (candidate.transform.position - referencePosition).sqrMagnitude;
            if (candidate.CompareTag("MainCamera"))
                score *= 0.5f;

            if (score < bestScore)
            {
                bestScore = score;
                bestCamera = candidate;
            }
        }

        return bestCamera;
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

    public static float GetAngleFromVector(Vector2 vector)
    {
        return Mathf.Abs(Mathf.Atan2(vector.x, vector.y) * Mathf.Rad2Deg) * Mathf.Sign(vector.x);
    }
}
