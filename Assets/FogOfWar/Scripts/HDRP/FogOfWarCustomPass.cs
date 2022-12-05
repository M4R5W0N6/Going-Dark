using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FOW
{
    public class FogOfWarCustomPass : CustomPass
    {
        Material fowMaterial;
        int fowPass;
        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
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
                    this.enabled = false;
                    Debug.Log("You must have a FogOfWarWorld object in your scene to use the FogOfWar Custom Pass");
                    return;
                }
                fow.Initialize();
            }


            fowMaterial = fow.fowMat;
            fowPass = fowMaterial.FindPass("FOW Pass");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            ctx.cmd.Blit(ctx.cameraColorBuffer, ctx.cameraColorBuffer, fowMaterial, fowPass);
        }
    }
}