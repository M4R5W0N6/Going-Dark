using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;

namespace FOW
{
    [Serializable, VolumeComponentMenu("Pixel-Perfect Fog Of War/Fog Of War Effect")]
    public sealed class FogOfWarEffect : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        public BoolParameter enabled = new BoolParameter(false);

        Material fowMaterial;
        int fowPass;

        public bool IsActive() => fowMaterial != null && enabled.value && FogOfWarWorld.instance != null && FogOfWarWorld.instance.enabled;

        // Do not forget to add this post process in the Custom Post Process Orders list (Project Settings > HDRP Default Settings).
        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforeTAA;

        public override void Setup()
        {
            FogOfWarWorld fow;
            if (!Application.isPlaying)
            {
                return;
            }

            if (FogOfWarWorld.instance)
            {
                fow = FogOfWarWorld.instance;
            }
            else
            {
                fow = GameObject.FindObjectOfType<FogOfWarWorld>();
                if (!fow)
                {
                    //this.enabled = false;
                    Debug.Log("You must have a FogOfWarWorld object in your scene to use the FogOfWar Custom Pass");
                    return;
                }
                fow.Initialize();
            }

            fowMaterial = fow.fowMat;
            fowPass = fowMaterial.FindPass("FOW Pass");
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (fowMaterial == null)
                return;

            cmd.Blit(source, destination, fowMaterial, fowPass);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(fowMaterial);
        }
    }
}