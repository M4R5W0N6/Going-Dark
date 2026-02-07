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

        //Material fowMaterial;
        //int fowPass;

        //public bool IsActive() => fowMaterial != null && enabled.value && FogOfWarWorld.instance != null && FogOfWarWorld.instance.enabled;
        public bool IsActive() => enabled.value && FogOfWarWorld.instance != null && FogOfWarWorld.instance.enabled;

        // Do not forget to add this post process in the Custom Post Process Orders list (Project Settings > HDRP Default Settings).
        //public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforeTAA;
        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterOpaqueAndSky;

        public override void Setup()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            //if (FogOfWarWorld.instance)
            //{
            //    fow = FogOfWarWorld.instance;
            //}
            //else
            //{
            //    fow = GameObject.FindObjectOfType<FogOfWarWorld>();
            //    if (!fow)
            //    {
            //        //this.enabled = false;
            //        Debug.Log("You must have a FogOfWarWorld object in your scene to use the FogOfWar Custom Pass");
            //        return;
            //    }
            //    //fow.Initialize();
            //}

            //fowMaterial = fow.FogOfWarMaterial;
            //fowPass = fowMaterial.FindPass("FOW Pass");
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (FogOfWarWorld.instance == null || !FogOfWarWorld.instance.enabled)
            {
                HDUtils.BlitCameraTexture(cmd, source, destination);
                //Debug.Log("returning");
                return;
            }

            FogOfWarWorld.OnPreRenderFog();

            if (FogOfWarWorld.instance.FogOfWarMaterial == null || FogOfWarWorld.instance.GetFowAppearance() == FogOfWarWorld.FogOfWarAppearance.None)
            {
                HDUtils.BlitCameraTexture(cmd, source, destination);
                return;
            }

            //cmd.Blit(source, destination, fowMaterial, fowPass);
            cmd.Blit(source, destination, FogOfWarWorld.instance.FogOfWarMaterial);
        }

        public override void Cleanup()
        {
            //CoreUtils.Destroy(fowMaterial);
        }
    }
}
