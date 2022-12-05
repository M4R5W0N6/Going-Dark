using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Profiling;
#endif

namespace FOW
{
    public class FogOfWarWorld : MonoBehaviour
    {
        public static FogOfWarWorld instance;

        public FogOfWarType FogType;
        
        public bool usingBlur;
        [Tooltip("how far to blur the edges. only used for soft fog types")]
        public float softenDistance = 3;
        public float unobscuredSoftenDistance = .25f;
        public bool useInnerSoften = true;
        public float innerSoftenAngle = 5;
        public FogOfWarFadeType FogFade;
        public float fogFadePower = 1;

        public FogOfWarAppearance FogAppearance;

        [Tooltip("The color of the fog")]
        public Color unknownColor = new Color(.65f, .65f, .65f);

        public float saturationStrength = 0;

        public float blurStrength = 1;
        //public float blurPixelOffset = 2.5f;
        [Range(0, 2)]
        public float blurDistanceScreenPercentMin = .1f;
        [Range(0, 2)]
        public float blurDistanceScreenPercentMax = 1;
        public int blurSamples = 6;

        public Texture2D fogTexture;
        public Vector2 fogTextureTiling = Vector2.one;

        [Range(0.001f, 1f)]
        public float SightExtraAmount = .01f;

        public RevealerUpdateMode revealerMode = RevealerUpdateMode.Every_Frame;
        [Tooltip("The number of revealers to update each frame. Only used when Revealer Mode is set to N_Per_Frame")]
        public int numRevealersPerFrame = 3;

        [Tooltip("The Max possible number of revealers. Keep this as low as possible to use less GPU memory")]
        public int maxPossibleRevealers = 256;
        [Tooltip("The Max possible number of segments per revealer. Keep this as low as possible to use less GPU memory")]
        public int maxPossibleSegmentsPerRevealer = 125;

        public GamePlane gamePlane = GamePlane.XZ;

        [HideInInspector]
        public Material fowMat;

        int maxCones;
        ComputeBuffer indicesBuffer;
        ComputeBuffer circleBuffer;
        ComputeBuffer anglesBuffer;
        int numCircles;

        int materialColorID = Shader.PropertyToID("_unKnownColor");
        int blurRadiusID = Shader.PropertyToID("_fadeOutDistance");
        int unobscuredBlurRadiusID = Shader.PropertyToID("_unboscuredFadeOutDistance");
        int extraRadiusID = Shader.PropertyToID("_extraRadius");
        int fadeTypeID = Shader.PropertyToID("_fadeType");
        int fadePowerID = Shader.PropertyToID("_fadePower");
        int saturationStrengthID = Shader.PropertyToID("_saturationStrength");
        int blurStrengthID = Shader.PropertyToID("_blurStrength");
        //int blurPixelOffsetID = Shader.PropertyToID("_blurPixelOffset");
        int blurPixelOffsetMinID = Shader.PropertyToID("_blurPixelOffsetMin");
        int blurPixelOffsetMaxID = Shader.PropertyToID("_blurPixelOffsetMax");
        int blurSamplesID = Shader.PropertyToID("_blurSamples");
        int blurPeriodID = Shader.PropertyToID("_samplePeriod");
        int fowTetureID = Shader.PropertyToID("_fowTexture");
        int fowTilingID = Shader.PropertyToID("_fowTiling");

        #region Data Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct CircleStruct
        {
            public Vector2 circleOrigin;
            public int startIndex;
            public int numSegments;
            public float circleRadius;
            public float circleHeight;
            public float visionHeight;
            public float unobscuredRadius;
            public int isComplete;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct ConeEdgeStruct
        {
            public float angle;
            public float length;
            public int cutShort;
        };
        public enum RevealerUpdateMode
        {
            Every_Frame,
            N_Per_Frame,
            Controlled_ElseWhere,
        };
        public enum FogOfWarType
        {
            No_Bleed,
            No_Bleed_Soft,
            Hard,
            Soft,
        };
        public enum FogOfWarFadeType
        {
            Smooth,
            Linear,
            Exponential,
        };
        public enum FogOfWarAppearance
        {
            Solid_Color,
            GrayScale,
            Blur,
            Texture_Sample,
        };
        public enum GamePlane
        {
            XZ,
            XY,
            ZY,
        };
        //public enum RenderPipelineType
        //{
        //    Built_In_Legacy,
        //    URP,
        //    HDRP,
        //};
        #endregion

        #region Unity Methods
        private void Awake()
        {
            Initialize();
        }
        private void OnEnable()
        {
            Initialize();
        }
        private void OnDestroy()
        {
            cleanup();
        }

        int currentIndex = 0;
        private void Update()
        {
            switch (revealerMode)
            {
                case RevealerUpdateMode.Every_Frame:
                    for (int i = 0; i < numCircles; i++)
                    {
                        revealers[i].CalculateLineOfSight();
                    }
                    break;
                case RevealerUpdateMode.N_Per_Frame:
                    for (int i = 0; i < Mathf.Clamp(numRevealersPerFrame, 0, numCircles); i++)
                    {
                        currentIndex = (currentIndex + 1) % numCircles;
                        revealers[currentIndex].CalculateLineOfSight();
                    }
                    break;
                case RevealerUpdateMode.Controlled_ElseWhere: break;
            }
        }
        #endregion

        void cleanup()
        {
            if (circleBuffer != null)
            {
                setAnglesBuffersJobHandle.Complete();
                //AnglesNativeArray.Dispose();
                indicesBuffer.Dispose();
                circleBuffer.Dispose();
                anglesBuffer.Dispose();
            }
            instance = null;
        }

        private JobHandle setAnglesBuffersJobHandle;
        private SetAnglesBuffersJob setAnglesBuffersJob;
        //NativeArray<ConeEdgeStruct> AnglesNativeArray;    //was used when using computebuffer.beginwrite. will be used again when unity fixes a bug internally
        ConeEdgeStruct[] anglesArray;

        public void Initialize()
        {
            if (instance)
                return;
            instance = this;

            maxCones = maxPossibleRevealers * maxPossibleSegmentsPerRevealer;

            revealers = new FogOfWarRevealer[maxPossibleRevealers];
            indicesBuffer = new ComputeBuffer(maxPossibleRevealers, Marshal.SizeOf(typeof(int)), ComputeBufferType.Default, ComputeBufferMode.SubUpdates);

            //circleBuffer = new ComputeBuffer(maxPossibleRevealers, Marshal.SizeOf(typeof(CircleStruct)), ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            circleBuffer = new ComputeBuffer(maxPossibleRevealers, Marshal.SizeOf(typeof(CircleStruct)), ComputeBufferType.Default);

            anglesArray = new ConeEdgeStruct[maxPossibleSegmentsPerRevealer];
            //AnglesNativeArray = new NativeArray<ConeEdgeStruct>(maxPossibleSegmentsPerRevealer, Allocator.Persistent);
            //anglesBuffer = new ComputeBuffer(maxCones, Marshal.SizeOf(typeof(ConeEdgeStruct)), ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            anglesBuffer = new ComputeBuffer(maxCones, Marshal.SizeOf(typeof(ConeEdgeStruct)), ComputeBufferType.Default);

            fowMat = new Material(Shader.Find("Hidden/FullScreen/FOW/SolidColor"));
            updateFogShader();
            updateFogConfiguration();

            setAnglesBuffersJob = new SetAnglesBuffersJob();

            foreach (FogOfWarRevealer revealer in revealersToRegister)
            {
                revealer.registerRevealer();
            }
            revealersToRegister.Clear();
        }

        //UnityEngine.LocalKeyword planeKeyWord;
        public static Vector3 upVector;
        public static Vector3 forwardVector;
        public void updateFogShader()
        {
            if (!Application.isPlaying)
                return;

            usingBlur = false;
            string shaderName = "Hidden/FullScreen/FOW";
            //switch (FogType)
            //{
            //    case FogOfWarType.No_Bleed: shaderName += "NoBleed"; break;
            //    case FogOfWarType.No_Bleed_Soft: shaderName += "NoBleedSoft"; usingBlur = true; break;
            //    case FogOfWarType.Hard: shaderName += "Hard"; break;
            //    case FogOfWarType.Soft: shaderName += "Soft"; usingBlur = true; break;
            //}
            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color: shaderName += "/SolidColor"; break;
                case FogOfWarAppearance.GrayScale: shaderName += "/GrayScale"; break;
                case FogOfWarAppearance.Blur: shaderName += "/Blur"; break;
                case FogOfWarAppearance.Texture_Sample: shaderName += "/TextureSample"; break;
            }
            fowMat.shader = Shader.Find(shaderName);

            fowMat.DisableKeyword("NO_BLEED");
            fowMat.DisableKeyword("NO_BLEED_SOFT");
            fowMat.DisableKeyword("HARD");
            fowMat.DisableKeyword("SOFT");
            switch (FogType)
            {
                case FogOfWarType.No_Bleed: fowMat.EnableKeyword("NO_BLEED"); break;
                case FogOfWarType.No_Bleed_Soft: fowMat.EnableKeyword("NO_BLEED_SOFT"); usingBlur = true; break;
                case FogOfWarType.Hard: fowMat.EnableKeyword("HARD"); break;
                case FogOfWarType.Soft: fowMat.EnableKeyword("SOFT"); usingBlur = true; break;
            }

            fowMat.DisableKeyword("PLANE_XZ");
            fowMat.DisableKeyword("PLANE_XY");
            fowMat.DisableKeyword("PLANE_ZY");
            switch (gamePlane)
            {
                case GamePlane.XZ:
                    //planeKeyWord = new LocalKeyword(fowMat.shader, "PLANE_XZ");
                    fowMat.EnableKeyword("PLANE_XZ");
                    upVector = Vector3.up;
                    break;
                case GamePlane.XY:
                    //planeKeyWord = new LocalKeyword(fowMat.shader, "PLANE_XY");
                    fowMat.EnableKeyword("PLANE_XY");
                    upVector = -Vector3.forward;
                    break;
                case GamePlane.ZY:
                    //planeKeyWord = new LocalKeyword(fowMat.shader, "PLANE_ZY");
                    fowMat.EnableKeyword("PLANE_ZY");
                    upVector = Vector3.right;
                    break;
            }

            //fowMat.EnableKeyword(planeKeyWord, true);

            fowMat.SetBuffer(Shader.PropertyToID("_ActiveCircleIndices"), indicesBuffer);
            fowMat.SetBuffer(Shader.PropertyToID("_CircleBuffer"), circleBuffer);
            fowMat.SetBuffer(Shader.PropertyToID("_ConeBuffer"), anglesBuffer);

            updateFogConfiguration();
        }
        public void updateFogConfiguration()
        {
            if (!Application.isPlaying)
                return;

            fowMat.SetColor(materialColorID, unknownColor);
            fowMat.SetFloat(blurRadiusID, softenDistance);
            fowMat.SetFloat(unobscuredBlurRadiusID, unobscuredSoftenDistance);
            fowMat.DisableKeyword("OUTER_SOFTEN");
            fowMat.DisableKeyword("INNER_SOFTEN");
            if (useInnerSoften)
                fowMat.EnableKeyword("INNER_SOFTEN");
            else
                fowMat.EnableKeyword("OUTER_SOFTEN");
            fowMat.SetFloat(Shader.PropertyToID("_fadeOutDegrees"), innerSoftenAngle);
            fowMat.SetFloat(extraRadiusID, SightExtraAmount);

            //fowMat.SetInt(fadeTypeID, (int)FogFade);
            fowMat.DisableKeyword("FADE_LINEAR");
            fowMat.DisableKeyword("FADE_SMOOTH");
            fowMat.DisableKeyword("FADE_EXP");
            switch (FogFade)
            {
                case FogOfWarFadeType.Linear:
                    fowMat.EnableKeyword("FADE_LINEAR");
                    break;
                case FogOfWarFadeType.Smooth:
                    fowMat.EnableKeyword("FADE_SMOOTH");
                    break;
                case FogOfWarFadeType.Exponential:
                    fowMat.EnableKeyword("FADE_EXP");
                    fowMat.SetFloat(fadePowerID, fogFadePower);
                    break;
            }
            

            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color:
                    {
                        break;
                    }
                case FogOfWarAppearance.GrayScale:
                    {
                        fowMat.SetFloat(saturationStrengthID, saturationStrength);
                        break;
                    }
                case FogOfWarAppearance.Blur:
                    {
                        fowMat.SetFloat(blurStrengthID, blurStrength);
                        //fowMat.SetFloat(blurPixelOffsetID, blurPixelOffset);
                        fowMat.SetFloat(blurPixelOffsetMinID, Screen.height * (blurDistanceScreenPercentMin / 100));
                        fowMat.SetFloat(blurPixelOffsetMaxID, Screen.height * (blurDistanceScreenPercentMax / 100));
                        fowMat.SetInt(blurSamplesID, blurSamples);
                        fowMat.SetFloat(blurPeriodID, (2 * Mathf.PI) / blurSamples);    //TAU = 2 * PI
                        break;
                    }
                case FogOfWarAppearance.Texture_Sample:
                    {
                        fowMat.SetTexture(fowTetureID, fogTexture);
                        fowMat.SetVector(fowTilingID, fogTextureTiling);
                        break;
                    }
            }
        }

        public FogOfWarRevealer[] revealers;
        public List<int> deregisteredIDs = new List<int>();
        int numDeregistered = 0;
        public static List<FogOfWarRevealer> revealersToRegister = new List<FogOfWarRevealer>();    //just used to prevent script execution order errors
        public int registerRevealer(FogOfWarRevealer newRevealer)
        {
#if UNITY_EDITOR
            Profiler.BeginSample("Register Revealer");
#endif
            numCircles++;
            fowMat.SetInt(Shader.PropertyToID("_NumCircles"), numCircles);

            int newID = numCircles - 1;
            revealers[newID] = newRevealer;
            if (numDeregistered > 0)
            {
                numDeregistered--;
                newID = deregisteredIDs[0];
                deregisteredIDs.RemoveAt(0);
            }

            newRevealer.indexID = numCircles - 1;

            _circleIndicesArray = indicesBuffer.BeginWrite<int>(numCircles - 1, 1);
            _circleIndicesArray[0] = newID;

            indicesBuffer.EndWrite<int>(1);

#if UNITY_EDITOR
            Profiler.EndSample();
#endif
            return newID;
        }
        public void deRegisterRevealer(FogOfWarRevealer toRemove)
        {
#if UNITY_EDITOR
            Profiler.BeginSample("De-Register Revealer");
#endif
            int index = toRemove.indexID;

            deregisteredIDs.Add(toRemove.fogOfWarID);
            numDeregistered++;

            numCircles--;

            FogOfWarRevealer toSwap = revealers[numCircles];

            if (toRemove != toSwap)
            {
                revealers[index] = toSwap;

                _circleIndicesArray = indicesBuffer.BeginWrite<int>(index, 1);
                _circleIndicesArray[0] = toSwap.fogOfWarID;
                toSwap.indexID = index;

                indicesBuffer.EndWrite<int>(1);
            }

            fowMat.SetInt(Shader.PropertyToID("_NumCircles"), numCircles);
#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }

        public static List<FogOfWarHider> hiders = new List<FogOfWarHider>();
        public static int numHiders;

        CircleStruct[] circleDataToSet = new CircleStruct[1];
        public void updateCircle(int id, CircleStruct data, float[] radii, float[] distances, bool[] hits)
        {
#if UNITY_EDITOR
            Profiler.BeginSample("write to compute buffers");
#endif
            //setAnglesBuffersJobHandle.Complete();
            data.startIndex = id * maxPossibleSegmentsPerRevealer;
            circleDataToSet[0] = data;
            circleBuffer.SetData(circleDataToSet, 0, id, 1);
            //_circleArray = circleBuffer.BeginWrite<CircleStruct>(id, 1);
            //_circleArray[0] = data;
            //circleBuffer.EndWrite<CircleStruct>(1);

            if (radii.Length > maxPossibleSegmentsPerRevealer)
            {
                Debug.LogError($"the revealer is trying to register {radii.Length} segments. this is more than was set by maxPossibleSegmentsPerRevealer");
                return;
            }
            for (int i = 0; i < radii.Length; i++)
            {
                anglesArray[i].angle = radii[i];
                anglesArray[i].length = distances[i];
                anglesArray[i].cutShort = hits[i] ? 1 : 0;
                //AnglesNativeArray[i] = anglesArray[i];
            }

            anglesBuffer.SetData(anglesArray, 0, id * maxPossibleSegmentsPerRevealer, radii.Length);
            //the following lines of code should work in theory, however due to a unity bug, are going to be put on hold for a little bit.
            //_angleArray = anglesBuffer.BeginWrite<ConeEdgeStruct>(id * maxPossibleSegmentsPerRevealer, radii.Length);
            //setAnglesBuffersJob.AnglesArray = _angleArray;
            //setAnglesBuffersJob.Angles = AnglesNativeArray;
            //setAnglesBuffersJobHandle = setAnglesBuffersJob.Schedule(radii.Length, 128);
            //setAnglesBuffersJobHandle.Complete();
            //anglesBuffer.EndWrite<ConeEdgeStruct>(radii.Length);

#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }

        NativeArray<int> _circleIndicesArray;
        NativeArray<CircleStruct> _circleArray;
        NativeArray<ConeEdgeStruct> _angleArray;

        [BurstCompile(CompileSynchronously = true)]
        private struct SetAnglesBuffersJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ConeEdgeStruct> Angles;
            [WriteOnly]
            public NativeArray<ConeEdgeStruct> AnglesArray;

            public void Execute(int index)
            {
                AnglesArray[index] = Angles[index];
            }
        }

        /// <summary>
        /// Test if provided point is currently visible.
        /// </summary>
        public static bool TestPointVisibility(Vector3 point)
        {
            for (int i = 0; i < instance.numCircles; i++)
            {
                if (instance.revealers[i].TestPoint(point))
                    return true;
            }
            return false;
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(FogOfWarWorld))]
    public class FogOfWarWorldEditor : Editor
    {
        static class Styles
        {
            public static readonly GUIStyle rightLabel = new GUIStyle("RightLabel");
        }
        string[] FogTypeOptions = new string[]
        {
            "No Bleed", "No Bleed Soft", "Hard", "Soft"
        };
        string[] FogAppearanceOptions = new string[]
        {
            "Solid Color", "Gray Scale", "Blur", "Texture Sample"
        };
        string[] FogFadeOptions = new string[]
        {
            "Smooth", "Linear", "Exponential"
        };
        string[] RevealerModeOptions = new string[]
        {
            "Every Frame", "N Per Frame", "Controlled Elsewhere"
        };
        string[] GamePlaneOptions = new string[]
        {
            "XZ", "XY", "ZY"
        };
        public override void OnInspectorGUI()
        {
            //DrawDefaultInspector();
            FogOfWarWorld fow = (FogOfWarWorld)target;

            EditorGUILayout.LabelField("Customization Options:");
            FogOfWarWorld.FogOfWarType fogType = fow.FogType;
            int selected = (int)fogType;
            selected = EditorGUILayout.Popup("Fog Type", selected, FogTypeOptions);
            fogType = (FogOfWarWorld.FogOfWarType)selected;
            if (fow.FogType != fogType)
            {
                fow.FogType = fogType;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                fow.updateFogShader();
            }
            if (fow.FogType == FogOfWarWorld.FogOfWarType.No_Bleed_Soft || fow.FogType == FogOfWarWorld.FogOfWarType.Soft)
            {
                float softenDist = fow.softenDistance;
                float newSoftenDist = EditorGUILayout.FloatField("Soften Distance: ", softenDist);
                if (newSoftenDist != softenDist)
                {
                    fow.softenDistance = newSoftenDist;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                softenDist = fow.unobscuredSoftenDistance;
                newSoftenDist = EditorGUILayout.FloatField("Un-Obscured area Soften Distance: ", softenDist);
                if (newSoftenDist != softenDist)
                {
                    fow.unobscuredSoftenDistance = newSoftenDist;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                bool innerSoften = fow.useInnerSoften;
                bool newinnerSoften = EditorGUILayout.Toggle("Soften Inner Edge? (BETA!)", innerSoften);
                if (newinnerSoften != innerSoften)
                {
                    fow.useInnerSoften = newinnerSoften;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
                if (newinnerSoften)
                {
                    softenDist = fow.innerSoftenAngle;
                    newSoftenDist = EditorGUILayout.FloatField("Inner Soften Angle: ", softenDist);
                    if (newSoftenDist != softenDist)
                    {
                        fow.innerSoftenAngle = newSoftenDist;
                        Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                        fow.updateFogConfiguration();
                    }
                }

                FogOfWarWorld.FogOfWarFadeType fadeType = fow.FogFade;
                selected = (int)fadeType;
                selected = EditorGUILayout.Popup("Fade Type", selected, FogFadeOptions);
                fadeType = (FogOfWarWorld.FogOfWarFadeType)selected;
                if (fow.FogFade != fadeType)
                {
                    fow.FogFade = fadeType;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
                if (fadeType == FogOfWarWorld.FogOfWarFadeType.Exponential)
                {
                    float fadeExp = fow.fogFadePower;
                    float newfadeExp = EditorGUILayout.FloatField("Fade Exponent: ", fadeExp);
                    if (fadeExp != newfadeExp)
                    {
                        fow.fogFadePower = newfadeExp;
                        Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                        fow.updateFogConfiguration();
                    }
                }
            }

            FogOfWarWorld.FogOfWarAppearance fogAppearance = fow.FogAppearance;
            selected = (int)fogAppearance;
            selected = EditorGUILayout.Popup("Fog Appearance", selected, FogAppearanceOptions);
            fogAppearance = (FogOfWarWorld.FogOfWarAppearance)selected;
            if (fow.FogAppearance != fogAppearance)
            {
                fow.FogAppearance = fogAppearance;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                fow.updateFogShader();
            }

            if (fow.FogAppearance == FogOfWarWorld.FogOfWarAppearance.Solid_Color)
            {
                Color unknownColor = fow.unknownColor;
                Color newColor = EditorGUILayout.ColorField("Unknown Area Color: ", unknownColor);
                if (unknownColor != newColor)
                {
                    fow.unknownColor = newColor;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
            }
            else if (fow.FogAppearance == FogOfWarWorld.FogOfWarAppearance.GrayScale)
            {
                Color unknownColor = fow.unknownColor;
                Color newColor = EditorGUILayout.ColorField("Unknown Area Color: ", unknownColor);
                if (unknownColor != newColor)
                {
                    fow.unknownColor = newColor;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                float oldStrength = fow.saturationStrength;
                float newStrength = EditorGUILayout.Slider("Unknown Area Saturation Strength: ", oldStrength, 0, 1);
                if (oldStrength != newStrength)
                {
                    fow.saturationStrength = newStrength;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
            }
            else if (fow.FogAppearance == FogOfWarWorld.FogOfWarAppearance.Blur)
            {
                Color unknownColor = fow.unknownColor;
                Color newColor = EditorGUILayout.ColorField("Unknown Area Color: ", unknownColor);
                if (unknownColor != newColor)
                {
                    fow.unknownColor = newColor;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                float oldBlur = fow.blurStrength;
                float newBlur = EditorGUILayout.Slider("Unknown Area Blur Strength: ", oldBlur, -1, 1);
                if (oldBlur != newBlur)
                {
                    fow.blurStrength = newBlur;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                //float oldBlurOffset = fow.blurPixelOffset;
                //float newBlurOffset = EditorGUILayout.Slider("Unknown Area Blur Pixel Offset: ", oldBlurOffset, 1.5f, 10);
                //if (oldBlurOffset != newBlurOffset)
                //{
                //    fow.blurPixelOffset = newBlurOffset;
                //    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                //    fow.updateFogConfiguration();
                //}
                float oldBlurOffset = fow.blurDistanceScreenPercentMin;
                float newBlurOffset = EditorGUILayout.Slider("Min Screen Percent: ", oldBlurOffset, 0, 2);
                if (oldBlurOffset != newBlurOffset)
                {
                    fow.blurDistanceScreenPercentMin = newBlurOffset;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                oldBlurOffset = fow.blurDistanceScreenPercentMax;
                newBlurOffset = EditorGUILayout.Slider("Max Screen Percent: ", oldBlurOffset, 0, 2);
                if (oldBlurOffset != newBlurOffset)
                {
                    fow.blurDistanceScreenPercentMax = newBlurOffset;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                int oldBlurSamples = fow.blurSamples;
                int newBlurSamples = EditorGUILayout.IntSlider("Num Blur Samples: ", oldBlurSamples, 6, 18);
                if (oldBlurSamples != newBlurSamples)
                {
                    fow.blurSamples = newBlurSamples;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
            }
            else if (fow.FogAppearance == FogOfWarWorld.FogOfWarAppearance.Texture_Sample)
            {
                Texture2D oldTexture = fow.fogTexture;
                Texture2D newTexture = (Texture2D)EditorGUILayout.ObjectField("Fog Of War Texture: ", oldTexture, typeof(Texture2D), false);
                if (newTexture != oldTexture)
                {
                    fow.fogTexture = newTexture;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                Vector2 oldTiling = fow.fogTextureTiling;
                Vector2 newTiling = EditorGUILayout.Vector2Field("Tiling: ", oldTiling);
                if (oldTiling != newTiling)
                {
                    fow.fogTextureTiling = newTiling;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }

                Color unknownColor = fow.unknownColor;
                Color newColor = EditorGUILayout.ColorField("Texture Color Multiplier: ", unknownColor);
                if (unknownColor != newColor)
                {
                    fow.unknownColor = newColor;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                    fow.updateFogConfiguration();
                }
            }

            float oldExtraSightAmount = fow.SightExtraAmount;
            float newExtraSightAmount = EditorGUILayout.Slider("Extra Sight Distance: ", oldExtraSightAmount, 0, 1);
            if (oldExtraSightAmount != newExtraSightAmount)
            {
                fow.SightExtraAmount = newExtraSightAmount;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                fow.updateFogConfiguration();
            }
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Utility Options (cant be changed at runtime)");

            FogOfWarWorld.RevealerUpdateMode revealerMode = fow.revealerMode;
            selected = (int)revealerMode;
            selected = EditorGUILayout.Popup("Revealer Mode", selected, RevealerModeOptions);
            revealerMode = (FogOfWarWorld.RevealerUpdateMode)selected;
            if (fow.revealerMode != revealerMode)
            {
                fow.revealerMode = revealerMode;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
            }

            if (fow.revealerMode == FogOfWarWorld.RevealerUpdateMode.N_Per_Frame)
            {
                int numRevealersPerFrame = fow.numRevealersPerFrame;
                int newNumRevealersPerFrame = EditorGUILayout.IntField("Num Revealers Per Feame: ", numRevealersPerFrame);
                if (numRevealersPerFrame != newNumRevealersPerFrame)
                {
                    fow.numRevealersPerFrame = newNumRevealersPerFrame;
                    Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
                }
            }

            int maxNumRevealers = fow.maxPossibleRevealers;
            int newmaxNumRevealers = EditorGUILayout.IntField("Max Num Revealers: ", maxNumRevealers);
            if (maxNumRevealers != newmaxNumRevealers)
            {
                fow.maxPossibleRevealers = newmaxNumRevealers;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
            }

            int maxNumSegments = fow.maxPossibleSegmentsPerRevealer;
            int newmaxNumSegments = EditorGUILayout.IntField("Max Num Segments Per Revealer: ", maxNumSegments);
            if (newmaxNumSegments != maxNumSegments)
            {
                fow.maxPossibleSegmentsPerRevealer = newmaxNumSegments;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
            }

            FogOfWarWorld.GamePlane plane = fow.gamePlane;
            selected = (int)plane;
            selected = EditorGUILayout.Popup("Game Plane", selected, GamePlaneOptions);
            plane = (FogOfWarWorld.GamePlane)selected;
            if (fow.gamePlane != plane)
            {
                fow.gamePlane = plane;
                Undo.RegisterCompleteObjectUndo(fow, "Change FOW parameters");
            }
        }
    }
#endif
}