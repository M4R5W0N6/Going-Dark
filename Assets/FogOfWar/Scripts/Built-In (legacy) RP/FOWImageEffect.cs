using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FOW
{
    [RequireComponent(typeof(Camera))]
    public class FOWImageEffect : MonoBehaviour
    {
        Camera cam;

        //public bool isGL;
        private void Awake()
        {
            //isGL = SystemInfo.graphicsDeviceVersion.Contains("OpenGL");
            cam = GetComponent<Camera>();
            cam.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
        }

        private void OnPreRender()
        {
            if (!FogOfWarWorld.instance)
                return;
            //cam.depthTextureMode = DepthTextureMode.Depth;

            Matrix4x4 camToWorldMatrix = cam.cameraToWorldMatrix;

            //Matrix4x4 projectionMatrix = cam.projectionMatrix;
            //Matrix4x4 inverseProjectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true).inverse;

            //if (!isGL)
            //    inverseProjectionMatrix[1, 1] *= -1;
            //else
            //{
            //    inverseProjectionMatrix[3, 2] -= inverseProjectionMatrix[3, 3];
            //    inverseProjectionMatrix[3, 2] *= -1;
            //    inverseProjectionMatrix[3, 3] = 0;
            //}
            //Debug.Log(camToWorldMatrix);
            //Debug.Log(inverseProjectionMatrix);
            FogOfWarWorld.instance.fowMat.SetMatrix("_camToWorldMatrix", camToWorldMatrix);
            //FogOfWarWorld.instance.fowMat.SetMatrix("_inverseProjectionMatrix", inverseProjectionMatrix);
        }
        void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            if (!FogOfWarWorld.instance || !FogOfWarWorld.instance.enabled)
            {
                Graphics.Blit(src, dest);
                return;
            }

            Graphics.Blit(src, dest, FogOfWarWorld.instance.fowMat);
        }
    }
}