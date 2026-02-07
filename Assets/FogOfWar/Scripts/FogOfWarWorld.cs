using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using System;
using UnityEngine.Serialization;
using static FOW.FogOfWarRevealer3D;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Profiling;
using UnityEditor.Experimental.GraphView;


#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    [DefaultExecutionOrder(-100)]
    public class FogOfWarWorld : MonoBehaviour
    {

        #region SERIALIZED STUFF

        //RENDERING OPTIONS
        public FogOfWarType FogType = FogOfWarType.Soft;
        public FogOfWarFadeType FogFade = FogOfWarFadeType.Smoothstep;
        public float FogFadePower = 1;
        public FogOfWarBlendMode BlendType = FogOfWarBlendMode.Additive;

        #region DITHERING

        [Tooltip("Uses dithering instead of true opacity.")]
        public bool UseDithering = false;
        public float DitherSize = 20;

        #endregion

        [Tooltip("Prevents Z-Fighting by allowing fog to slightly expand past its actual vision radius." +
            "\n\nYou can also use negative values to prevent unwanted bleeding, in the case you use pixelated fog, or texture fog with a low resolution.")]
        public float SightExtraAmount = .01f;
        [Tooltip("Controls softening for Revealer Extra Sight Distance")]
        public float EdgeSoftenDistance = .1f;

        [Tooltip("Controls the maximum distance FOW is rendered")]
        public float MaxFogDistance = 10000f;

        //SHADER OPTIONS
        [SerializeField] private FogOfWarAppearance FogAppearance;

        [Tooltip("The color of the fog")]
        public Color UnknownColor = new Color(.35f, .35f, .35f);

        #region Grayscale Sample Options

        public float SaturationStrength = 0;

        #endregion

        #region Blur Fog Shader Options

        public float BlurStrength = 1;
        //public float blurPixelOffset = 2.5f;
        [Range(0, 2)]
        public float BlurDistanceScreenPercentMin = .1f;
        [Range(0, 2)]
        public float BlurDistanceScreenPercentMax = 1;
        public int BlurSamples = 6;

        #endregion

        #region Texture Sample Shader Options

        public Texture2D FogTexture;
        public bool UseTriplanar = true;
        public Vector2 FogTextureTiling = Vector2.one;
        public Vector2 FogScrollSpeed = Vector2.one;

        #endregion

        #region Outline Shader Options

        public float OutlineThickness = .1f;

        #endregion

        //EXTRA RENDERING OPTIONS
        #region PIXELATION

        public bool PixelateFog = false;
        public bool WorldSpacePixelate = false;
        public float PixelDensity = 2f;
        public bool RoundRevealerPosition = false;
        public Vector2 PixelGridOffset;

        #endregion

        #region WORLD BOUNDS

        public bool UseWorldBounds;
        public float WorldBoundsSoftenDistance = 1f;
        public float WorldBoundsInfluence = 1;

        #endregion

        public bool InvertFowEffect;

        [Tooltip("Allows fog to slightly bleed past obstacle edges in an arc shape")]
        public bool AllowBleeding = false;

        //SAMPLING MODE OPTIONS
        [Tooltip("Controls how fog is sampled in the fullscreen shader" +
            "\n\nPixel-Perfect- Fog is calculated per-pixel in screen space." +
            "\n  Pros:\n    -This mode allows for unlimited world sized with full resolution fog." +
            "\n  Cons:\n    -Cannot use temporal based effects, like fog memory/regrow/retention." +
            "\n\nTexture Storage- This mode uses a more traditional method of rendering FOW. It first does the fog calculations on a Render Texture, then samples that render texture in the fullscreen shader." +
            "\n  Pros:\n    -Can use extra fog effects, such as fog memory/regrow/retention." +
            "\n  Cons:\n    -Requires rendering to a render texture, which uses gpu memory.\n    -Resolution bound, large worlds requires rendering the fog texture at higher resolutions to avoid seeing noticeable grids.")]
        public FogSampleMode FOWSamplingMode = FogSampleMode.Pixel_Perfect;

        #region TEXTURE SAMPLE FOG OPTIONS

        [Tooltip("When true, hiders will sample the Texture Storage fog, instead of using a revealers direct line of sight")]
        public bool HidersUseFogTexture = true;
        [Tooltip("The threshold at which hiders are seen with the fog texture.")]
        [Range(0,1f)]
        public float HiderSeenThreshold = .5f;
        [Tooltip("When true, sampling the fog texture on the CPU will be much faster, but will calculate it even when its not needed." +
            "\n\nYou should definitely keep this true if you need to sample the fog texture from code frequently." +
            "\n\nIf Hiders Use Fog Texture is true, then this is also true.")]
        public bool AsyncReadbackFogDataToCpu = false;

        public bool UseConstantBlur = true;
        public int ConstantTextureBlurQuality = 2;
        public float ConstantTextureBlurAmount = 0.75f;

        #endregion

        //FOW TEXTURE OPTIONS
        public bool UseMiniMap;
        //public int FowTextureMsaa = 8;
        public int FowResX = 512;
        public int FowResY = 512;

        #region REGROW OPTIONS

        public bool UseRegrow;
        public bool RevealerFadeIn = false;
        public float RevealerFadeInSpeed = .5f;

        public bool RevealerFadeOut = false;
        [FormerlySerializedAs("FogRegrowSpeed")]
        public float RevealerFadeOutSpeed = .5f;

        public float InitialFogExplorationValue = 0;
        public float MaxFogRegrowAmount = .3f;

        #endregion

        //WORLD BOUNDS
        public Bounds WorldBounds = new Bounds(Vector3.zero, Vector3.one);

        //CONFIG AND OPTIMIZATION
        [Tooltip("Changes where Fog of War updates revealers, calculates hiders, and updates the fog texture." +
            "\n\nUpdate: Updates happen in update" +
            "\n\nLate Update: Updates happen in Late Update" +
            "\n\nStart in update, Finish in late update: Since revealers use the c# jobs system, we can use this option to let the job run for as long as possible before completing the job manually.")]
        public FowUpdateMethod UpdateMethod = FowUpdateMethod.LateUpdate;

        [Tooltip("Controls how revealers are calculated" +
            "\n\nEvery Frame- Every revealer is calculated every frame" +
            "\n\nTime Spliced- Revealers take turns being calculated. You can choose how many are calculated per-frame." +
            "\n\nManual Updates- Revealers will not be automatically updated. Instead, you can update them manually in code.")]
        [FormerlySerializedAs("revealerMode")]
        public RevealerUpdateMethod RevealerUpdateMode = RevealerUpdateMethod.N_Per_Frame;

        [Tooltip("The number of revealers to update each frame. Only used when Revealer Mode is set to 'Time Spliced'")]
        public int MaxNumRevealersPerFrame = 25;

        [SerializeField] public bool UseSpatialAcceleration = true;
        [Tooltip("The cell size used for the spatial hash grid. The best value to use for this will be your average revealers radius (including soften distance) times two.")]
        [SerializeField] private int SpatialHashGridSize = 32;
        [Tooltip("How many buckets to use when spatial hashing. more buckets = less collision")]
        [SerializeField] private int NumSpatialHashBuckets = 1024;

        //utility options
        [Tooltip("The Max possible number of revealers. Keep this as low as possible to use less GPU memory")]
        public int MaxPossibleRevealers = 256;
        [Tooltip("The Max possible number of segments per revealer. Keep this as low as possible to use less GPU memory")]
        public int MaxPossibleSegmentsPerRevealer = 128;
        [Tooltip("The Max possible number of Hiders. Keep this as low as possible to use less memory. It will automatically resize if you add too many hiders, but that can cause a hitch!")]
        public int MaxPossibleHiders = 512;
        public bool is2D;
        [FormerlySerializedAs("gamePlane")]
        public GamePlane GamePlaneOrientation = GamePlane.XZ;

        #endregion

        #region RUNTIME STUFF

        public static FogOfWarWorld instance;

        public static bool UsingSoftening;

        public Material FogOfWarMaterial;
        public Material FowTextureMaterial;
        static RenderTexture FOW_RT;
        static RenderTexture FOW_TEMP_RT;

        static int TotalMaximumSightSegments;
        public static ComputeBuffer ActiveRevealerIndicesBuffer;    //only used for non-spatial hash path. remove when spatial hashing is battle tested.
        public static ComputeBuffer RevealerInfoBuffer;
        public static ComputeBuffer RevealerDataBuffer;
        public static ComputeBuffer AnglesBuffer;

        #region REVEALERS

        public static FogOfWarRevealer[] ActiveRevealers;
        public static FogOfWarRevealer[] UnsortedRevealers;
        public static int NumActiveRevealers;
        public static int numDynamicRevealers;
        public static List<int> DeregisteredRevealerIDs = new List<int>();
        private static int numDeregisteredRevealers = 0;
        public static List<FogOfWarRevealer> RevealersToRegister = new List<FogOfWarRevealer>();     //revealers to register once fow world exists

        #endregion

        #region HIDERS

        public static FogOfWarHider[] ActiveHiders;
        public static FogOfWarHider[] UnsortedHiders;
        public static int[] ActiveHiderIndices;
        public static int NumActiveHiders;
        public static List<PartialHider> PartialHiders = new List<PartialHider>();
        public static List<int> DeregisteredHiderIDs = new List<int>();
        private static int numDeregisteredHiders = 0;
        public static List<FogOfWarHider> HidersToRegister = new List<FogOfWarHider>(); //hiders to register once fow world exists

        #endregion

        private static int[] indiciesDataToSet = new int[1];
        private static bool UsingFowTexture;

        private AsyncFogTextureReader _asyncFogTextureReader;
        private static bool revealerSeesHiders;
        //private static bool UsingFogAsyncReadback;

        #endregion

        #region SHADER IDS

        //compute buffer shader ids
        int activeRevealerIndicesID = Shader.PropertyToID("_ActiveRevealerIndices");
        int revealerInfoID = Shader.PropertyToID("_RevealerInfoBuffer");
        int revealerDataID = Shader.PropertyToID("_RevealerDataBuffer");
        int sightSegmentBufferID = Shader.PropertyToID("_SightSegmentBuffer");

        static int FowEffectStrengthID = Shader.PropertyToID("FowEffectStrength");
        int numRevealersID = Shader.PropertyToID("_NumRevealers");
        int materialColorID = Shader.PropertyToID("_unKnownColor");
        int extraRadiusID = Shader.PropertyToID("_extraRadius");
        int maxDistanceID = Shader.PropertyToID("_maxDistance");
        int fadePowerID = Shader.PropertyToID("_fadePower");
        int saturationStrengthID = Shader.PropertyToID("_saturationStrength");
        int blurStrengthID = Shader.PropertyToID("_blurStrength");
        int blurPixelOffsetMinID = Shader.PropertyToID("_blurPixelOffsetMin");
        int blurPixelOffsetMaxID = Shader.PropertyToID("_blurPixelOffsetMax");
        int blurSamplesID = Shader.PropertyToID("_blurSamples");
        int blurPeriodID = Shader.PropertyToID("_samplePeriod");
        int fowTetureID = Shader.PropertyToID("_fowTexture");
        int fowTilingID = Shader.PropertyToID("_fowTiling");
        int fowSpeedID = Shader.PropertyToID("_fowScrollSpeed");

        #endregion

        #region Profiler Markers

#if UNITY_EDITOR

        static readonly ProfilerMarker UploadToGpuProfileMarker = new ProfilerMarker("Write to compute buffers");
        static readonly ProfilerMarker HiderBucketsProfileMarker = new ProfilerMarker("Update Hider Buckets");
        static readonly ProfilerMarker RegisterRevealersProfileMarker = new ProfilerMarker("Register Revealer");
        static readonly ProfilerMarker DeRegisterRevealersProfileMarker = new ProfilerMarker("De-Register Revealer");
        static readonly ProfilerMarker RegisterHiderProfileMarker = new ProfilerMarker("Register Hider");
        static readonly ProfilerMarker DeRegisterHiderProfileMarker = new ProfilerMarker("De-Register Hider");
        static readonly ProfilerMarker LoopRevealersProfileMarker = new ProfilerMarker("Loop Revealers");
        static readonly ProfilerMarker FogTextureBlitProfileMarker = new ProfilerMarker("Fog Texture Blit");
        static readonly ProfilerMarker TextureHiderSampleProfileMarker = new ProfilerMarker("Fog Hider Sample (from texture)");

#endif

        #endregion

        #region Data Structures

        [StructLayout(LayoutKind.Sequential)]
        public struct RevealerInfoStruct
        {
            public int StartIndex;

            public float RevealerVisionRadius;
            public float RevealerVisionRadiusFade;

            public float innerSoftenThreshold;
            public float invInnerSoftenThreshold;

            public float UnobscuredRadius;
            public float UnobscuredSoftenRadius;
            
            public float VisionHeight;
            public float VisionHeightFade;
            public float Opacity;
            public int UseOcclusion;   //0 false, 1 true
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct RevealerDataStruct
        {
            public float RevealerTotalVisionRadius;
            public Vector2 RevealerPosition;
            public float RevealerHeight;
            public int NumSegments;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct GpuSightSegment
        {
            public float2 direction;
            public float length;
        };

        public enum FowUpdateMethod
        {
            Update,
            LateUpdate,
            StartInUpdateFinishInLateUpdate
        };

        public enum RevealerUpdateMethod
        {
            Every_Frame,
            N_Per_Frame,
            Controlled_ElseWhere,
        };

        public enum FogSampleMode
        {
            Pixel_Perfect,
            Texture,
            Both,
        };

        public enum FogOfWarType
        {
            //No_Bleed,
            //No_Bleed_Soft,
            Hard,
            Soft,
        };

        public enum FogOfWarFadeType
        {
            Linear,
            Exponential,
            Smooth,
            Smoother,
            Smoothstep,
        };

        public enum FogOfWarBlendMode
        {
            Max,
            Additive,
        };

        public enum FogOfWarAppearance
        {
            Solid_Color,
            GrayScale,
            Blur,
            Texture_Sample,
            Outline,
            None
        };

        public enum GamePlane
        {
            XZ,
            XY,
            ZY,
        };
        #endregion

        #region Unity Methods

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnLoad()
        {
            ResetStatics();
        }

        static void ResetStatics()
        {
            instance = null;
            PartialHiders = new List<PartialHider>();
            
            NumActiveRevealers = 0;
            numDynamicRevealers = 0;
            RevealersToRegister = new List<FogOfWarRevealer>();
            DeregisteredRevealerIDs = new List<int>();
            numDeregisteredRevealers = 0;

            NumActiveHiders = 0;
            HidersToRegister = new List<FogOfWarHider>();
            DeregisteredHiderIDs = new List<int>();
            numDeregisteredHiders = 0;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // see the unity bug workaround section
            UnityBugWorkaround.OnAssetPostProcess += ReInitializeFOW;
#endif
            Initialize();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            // see the unity bug workaround section
            UnityBugWorkaround.OnAssetPostProcess -= ReInitializeFOW;
#endif
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        int currentIndex = 0;
        private void Update()
        {
            if (UpdateMethod == FowUpdateMethod.Update)
            {
                CalculateFOWPhaseOne();
                CalculateFOWPhaseTwo();
            }
            else if (UpdateMethod == FowUpdateMethod.StartInUpdateFinishInLateUpdate)
                CalculateFOWPhaseOne();
        }

        private void LateUpdate()
        {
            if (UpdateMethod == FowUpdateMethod.LateUpdate)
            {
                CalculateFOWPhaseOne();
                CalculateFOWPhaseTwo();
            }
            else if (UpdateMethod == FowUpdateMethod.StartInUpdateFinishInLateUpdate)
                CalculateFOWPhaseTwo();
        }

        #endregion

        #region FOW/Revealer updates
        
        void CalculateFOWPhaseOne()
        {
            if (NumActiveRevealers <= 0)
                return;

            if (revealerSeesHiders && SparseRevealerGrid.SpatialAccelerationActive)
            {
#if UNITY_EDITOR
                HiderBucketsProfileMarker.Begin();
#endif
                for (int i = 0; i < NumActiveHiders; i++)
                {
                    ActiveHiders[i].UpdateBuckets();
                }
#if UNITY_EDITOR
                HiderBucketsProfileMarker.End();
#endif
            }

#if UNITY_EDITOR
            LoopRevealersProfileMarker.Begin();
#endif

            switch (RevealerUpdateMode)
            {
                case RevealerUpdateMethod.Every_Frame:
                    for (int i = 0; i < NumActiveRevealers; i++)
                    {
                        var revealer = ActiveRevealers[i];
                        if (!revealer.CurrentlyStaticRevealer)
                            revealer.LineOfSightPhase1();
                        if (revealerSeesHiders)
                            revealer.RevealHiders();
                    }
                    break;
                case RevealerUpdateMethod.N_Per_Frame:
                    int index = currentIndex;
                    for (int i = 0; i < Mathf.Clamp(MaxNumRevealersPerFrame, 0, numDynamicRevealers); i++)
                    {
                        index = (index + 1) % NumActiveRevealers;

                        var revealer = ActiveRevealers[index];
                        if (!revealer.CurrentlyStaticRevealer)
                            revealer.LineOfSightPhase1();
                        else
                            i--;

                        if (revealerSeesHiders)
                            revealer.RevealHiders();
                    }
                    break;
                case RevealerUpdateMethod.Controlled_ElseWhere: break;
            }

            //FogOfWarRevealer.PostPhaseOne();

#if UNITY_EDITOR
            LoopRevealersProfileMarker.End();
#endif
        }

        void CalculateFOWPhaseTwo()
        {
            if (NumActiveRevealers > 0)
            {
#if UNITY_EDITOR
                LoopRevealersProfileMarker.Begin();
#endif
                switch (RevealerUpdateMode)
                {
                    case RevealerUpdateMethod.Every_Frame:
                        for (int i = 0; i < NumActiveRevealers; i++)
                        {
                            if (!ActiveRevealers[i].CurrentlyStaticRevealer)
                                ActiveRevealers[i].LineOfSightPhase2();
                        }
                        break;
                    case RevealerUpdateMethod.N_Per_Frame:
                        for (int i = 0; i < Mathf.Clamp(MaxNumRevealersPerFrame, 0, numDynamicRevealers); i++)
                        {
                            currentIndex = (currentIndex + 1) % NumActiveRevealers;
                            if (!ActiveRevealers[currentIndex].CurrentlyStaticRevealer)
                                ActiveRevealers[currentIndex].LineOfSightPhase2();
                            else
                                i--;
                        }
                        break;
                    case RevealerUpdateMethod.Controlled_ElseWhere: break;
                }

#if UNITY_EDITOR
                LoopRevealersProfileMarker.End();
#endif
            }

            RenderFogTexture();
        }

        public void RenderFogTexture()
        {
            if (!UsingFowTexture)
                return;
#if UNITY_EDITOR
            FogTextureBlitProfileMarker.Begin();
#endif
            if (SparseRevealerGrid.SpatialAccelerationActive)
                SparseRevealerGrid.FlattenAndUpload();

            if (UseRegrow)
            {
                Graphics.Blit(FOW_RT, FOW_TEMP_RT);
                Graphics.Blit(FOW_TEMP_RT, FOW_RT, FowTextureMaterial, 0);
            }
            else
                Graphics.Blit(null, FOW_RT, FowTextureMaterial, 0);

#if UNITY_EDITOR
            FogTextureBlitProfileMarker.End();
#endif

            bool revealHidersWithTexture = !revealerSeesHiders && NumActiveHiders != 0;
            if (AsyncReadbackFogDataToCpu || revealHidersWithTexture)
            {
#if UNITY_EDITOR
                TextureHiderSampleProfileMarker.Begin();
#endif
                
                _asyncFogTextureReader.Update(FOW_RT);
                if (revealHidersWithTexture)
                    _asyncFogTextureReader.SeekHiders();

#if UNITY_EDITOR
                TextureHiderSampleProfileMarker.End();
#endif
            }
        }

        #endregion

        #region Dumb Unity Bug Workaround :)
#if UNITY_EDITOR
        //BASICALLY, every time an asset is updated in the project folder, materials are losing the compute buffer data. 
        //So, im hooking onto asset post processing, and re-initializing the material with the necessary data
        public void ReInitializeFOW()
        {
            StartCoroutine(FixFowDebug());
        }

        IEnumerator FixFowDebug()
        {
            yield return new WaitForEndOfFrame();
            enabled = false;
            enabled = true;
            //FogOfWarMaterial.SetBuffer(Shader.PropertyToID("_ActiveRevealerIndices"), IndicesBuffer);
            //FogOfWarMaterial.SetBuffer(Shader.PropertyToID("_CircleBuffer"), CircleBuffer);
            //FogOfWarMaterial.SetBuffer(sightSegmentBufferID, AnglesBuffer);
            //UpdateMaterialProperties(FogOfWarMaterial);
        }
#endif
        #endregion

        #region Initialization/Cleanup

        void Cleanup()
        {
            if (FogOfWarMaterial != null)
                Destroy(FogOfWarMaterial);
            if (FogOfWarMaterial != null)
                Destroy(FowTextureMaterial);
            SetFowEffectStrength(0);

            for (int i = NumActiveRevealers - 1; i >= 0; i--)
            {
                FogOfWarRevealer revealer = ActiveRevealers[i];
                revealer.DeregisterRevealer();
                RevealersToRegister.Add(revealer);
            }

            for (int i = NumActiveHiders - 1; i >= 0; i--)
            {
                FogOfWarHider hider = ActiveHiders[i];
                hider.DeregisterHider();
                HidersToRegister.Add(hider);
            }

            if (RevealerDataBuffer != null)
            {
                //setAnglesBuffersJobHandle.Complete();
                //AnglesNativeArray.Dispose();
                ActiveRevealerIndicesBuffer.Dispose();
                RevealerInfoBuffer.Dispose();
                RevealerDataBuffer.Dispose();
                AnglesBuffer.Dispose();
            }

            if (FOW_RT != null)
            {
                FOW_RT.Release();
                Destroy(FOW_RT);
            }

            _asyncFogTextureReader?.Dispose();
            _asyncFogTextureReader = null;
            instance = null;
            SparseRevealerGrid.Cleanup();
        }

        public void Initialize()
        {
            if (instance != null)
                return;

            instance = this;

            SetFowEffectStrength(1);

            if (!is2D)
                FogOfWarRevealer3D.Projection = new PlaneProjection(GamePlaneOrientation);
            else
                FogOfWarRevealer3D.Projection = new PlaneProjection(GamePlane.XY);

            TotalMaximumSightSegments = MaxPossibleRevealers * MaxPossibleSegmentsPerRevealer;

            ActiveRevealers = new FogOfWarRevealer[MaxPossibleRevealers];
            UnsortedRevealers = new FogOfWarRevealer[MaxPossibleRevealers];
            ActiveHiders = new FogOfWarHider[MaxPossibleHiders];
            UnsortedHiders = new FogOfWarHider[MaxPossibleHiders];

            ActiveRevealerIndicesBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(int)), ComputeBufferType.Default);
            RevealerInfoBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(RevealerInfoStruct)), ComputeBufferType.Default);
            RevealerDataBuffer = new ComputeBuffer(MaxPossibleRevealers, Marshal.SizeOf(typeof(RevealerDataStruct)), ComputeBufferType.Default);

            SightSegmentsUploadData = new GpuSightSegment[MaxPossibleSegmentsPerRevealer];
            AnglesBuffer = new ComputeBuffer(TotalMaximumSightSegments, Marshal.SizeOf(typeof(GpuSightSegment)), ComputeBufferType.Default);

            //UpdateMaterialProperties(FogOfWarMaterial);
            if (UseMiniMap || FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both)
            {
                if (FowTextureMaterial != null)
                    Destroy(FowTextureMaterial);
                FowTextureMaterial = new Material(Shader.Find("Hidden/FullScreen/FOW/FOW_RT"));
                InitFOWRT();

                BindComputeBuffersToMaterial(FowTextureMaterial);
                UpdateMaterialProperties(FowTextureMaterial);
                FowTextureMaterial.EnableKeyword("IGNORE_HEIGHT");
            }

            if (FogOfWarMaterial != null)
                Destroy(FogOfWarMaterial);
            //FogOfWarMaterial = new Material(Shader.Find("Hidden/FullScreen/FOW/SolidColor"));
            SetFogShader();
            SwitchSpatialAccelerationMode(UseSpatialAcceleration);
            SwitchHidersUseFogTextureMode(HidersUseFogTexture);
            //ToggleFogTextureAsyncReadbackToCpu(AsyncReadbackFogDataToCpu);
            UpdateAllMaterialProperties();
            FowBoundsUpdated();

            //setAnglesBuffersJob = new SetAnglesBuffersJob();

            foreach (FogOfWarRevealer revealer in RevealersToRegister)
            {
                if (revealer != null)
                    revealer.RegisterRevealer();
            }
            RevealersToRegister.Clear();

            foreach (FogOfWarHider hider in HidersToRegister)
            {
                if (hider != null)
                    hider.RegisterHider();
            }
            HidersToRegister.Clear();
        }

        public void SwitchSpatialAccelerationMode(bool useSpatial)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (useSpatial && !SparseRevealerGrid.SpatialAccelerationActive)
            {
                SparseRevealerGrid.Initialize(NumSpatialHashBuckets, SpatialHashGridSize);
            }
            else if (!useSpatial && SparseRevealerGrid.SpatialAccelerationActive)
            {
                SparseRevealerGrid.Cleanup();
                EnableKeywordOnAllMaterials("USE_SPATIAL_HASHING", false);
            }
        }

        public void SwitchHidersUseFogTextureMode(bool useFogTextureToSeeHiders)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            HidersUseFogTexture = useFogTextureToSeeHiders;

            revealerSeesHiders = FOWSamplingMode == FogSampleMode.Pixel_Perfect || !HidersUseFogTexture;

            if (useFogTextureToSeeHiders)
            {
                for (int i = 0; i < NumActiveRevealers; i++)
                    ActiveRevealers[i].HiderSeeker.ClearRevealedList();
            }
            else
            {
                if (_asyncFogTextureReader != null)
                    _asyncFogTextureReader.UnseeAllHiders();
            }

            //re-initialize or dispose the async texture readback
            ToggleFogTextureAsyncReadbackToCpu(AsyncReadbackFogDataToCpu);
        }

        public void ToggleFogTextureAsyncReadbackToCpu(bool useAsyncReadback)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            AsyncReadbackFogDataToCpu = useAsyncReadback;

            useAsyncReadback |= !revealerSeesHiders;

            //UsingFogAsyncReadback = useAsyncFeedback;

            if (!useAsyncReadback && _asyncFogTextureReader != null)
            {
                _asyncFogTextureReader.UnseeAllHiders();
                _asyncFogTextureReader.Dispose();
                _asyncFogTextureReader = null;
            }
            if (useAsyncReadback && _asyncFogTextureReader == null)
            {
                _asyncFogTextureReader = new AsyncFogTextureReader();
                UpdateHiderSeenThresholdForFogTexture();
            }
        }

        public void UpdateHiderSeenThresholdForFogTexture()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            if (_asyncFogTextureReader == null)
                return;
            _asyncFogTextureReader.HiderSeeingThreshold = 1 - HiderSeenThreshold;
        }

        public static float3 UpVector;
        public static float3 ForwardVector;
        public void SetFogShader()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            UsingSoftening = false;
            string shaderName = "Hidden/FullScreen/FOW";
            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color: shaderName += "/SolidColor"; break;
                case FogOfWarAppearance.GrayScale: shaderName += "/GrayScale"; break;
                case FogOfWarAppearance.Blur: shaderName += "/Blur"; break;
                case FogOfWarAppearance.Texture_Sample: shaderName += "/TextureSample"; break;
                case FogOfWarAppearance.Outline: shaderName += "/Outline"; break;
                case FogOfWarAppearance.None: shaderName = "Hidden/BlitCopy"; break;
            }
            //FogOfWarMaterial.shader = Shader.Find(shaderName);
            FogOfWarMaterial = new Material(Shader.Find(shaderName));

            //if (FogAppearance == FogOfWarAppearance.None)
            //{
            //    FogOfWarMaterial = null;
            //    return;
            //}
            
#if UNITY_2021_2_OR_NEWER
#else
            //this was required in unity 2020.3.28. when updating to 2020.3.48, its no longer required. not sure what version fixes it exactly.
            //FogOfWarMaterial.EnableKeyword("_VS_NORMAL");   //this is only for urp/texture sample fog mode
#endif

            InitializeFogProperties(FogOfWarMaterial);
            UpdateMaterialProperties(FogOfWarMaterial);
            //SetMaterialBounds();
        }

        public void InitializeFogProperties(Material material)
        {
            material.DisableKeyword("IS_2D");
            if (!is2D)
            {
                switch (GamePlaneOrientation)
                {
                    case GamePlane.XZ:
                        //material.EnableKeyword("PLANE_XZ");
                        material.SetInt("_fowPlane", 1);
                        UpVector = Vector3.up;
                        break;
                    case GamePlane.XY:
                        //material.EnableKeyword("PLANE_XY");
                        material.SetInt("_fowPlane", 2);
                        UpVector = -Vector3.forward;
                        break;
                    case GamePlane.ZY:
                        //material.EnableKeyword("PLANE_ZY");
                        material.SetInt("_fowPlane", 3);
                        UpVector = Vector3.right;
                        break;
                }
            }
            else
            {
                UpVector = -Vector3.forward;
                material.EnableKeyword("IS_2D");

                material.SetInt("_fowPlane", 0);
            }

            BindComputeBuffersToMaterial(material);
            BindSpatialHashComputeBuffersToMaterial(material);
        }

        public void BindComputeBuffersToMaterial(Material material)
        {
            material.SetBuffer(activeRevealerIndicesID, ActiveRevealerIndicesBuffer);
            material.SetBuffer(revealerInfoID, RevealerInfoBuffer);
            material.SetBuffer(revealerDataID, RevealerDataBuffer);
            material.SetBuffer(sightSegmentBufferID, AnglesBuffer);
        }

        public void BindSpatialHashComputeBuffersToAllMaterials()
        {
            if (!Application.isPlaying)
                return;
            if (!SparseRevealerGrid.SpatialAccelerationActive)
                return;

            if (FogOfWarMaterial != null)
                BindSpatialHashComputeBuffersToMaterial(FogOfWarMaterial);
            if (FowTextureMaterial != null)
                BindSpatialHashComputeBuffersToMaterial(FowTextureMaterial);

            foreach (PartialHider hider in PartialHiders)
                BindSpatialHashComputeBuffersToMaterial(hider.HiderMaterial);
        }

        public void BindSpatialHashComputeBuffersToMaterial(Material material)
        {
            material.DisableKeyword("USE_SPATIAL_HASHING");
            if (!SparseRevealerGrid.SpatialAccelerationActive)
                return;
            SparseRevealerGrid.BindPropertiesToMaterial(material);

            material.EnableKeyword("USE_SPATIAL_HASHING");
        }

        public void UpdateAllMaterialProperties()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            UsingFowTexture = UseMiniMap || FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both;

            UpdateMaterialProperties(FogOfWarMaterial);
            if (FowTextureMaterial != null)
                UpdateMaterialProperties(FowTextureMaterial);

            foreach (PartialHider hider in PartialHiders)
                UpdateMaterialProperties(hider.HiderMaterial);

            //SetMaterialBounds();
        }

        public void UpdateMaterialProperties(Material material)
        {
#if UNITY_EDITOR
            if (material == null)   //fix for "Enter Playmode Options"
                return;
#endif
            material.DisableKeyword("HARD");
            material.DisableKeyword("SOFT");
            bool newUsingSoftening = false;
            switch (FogType)
            {
                case FogOfWarType.Hard: material.EnableKeyword("HARD"); break;
                case FogOfWarType.Soft: material.EnableKeyword("SOFT"); newUsingSoftening = true; break;
            }

            if (UsingSoftening != newUsingSoftening)
            {
                UsingSoftening = newUsingSoftening;
                for (int i = 0; i < NumActiveRevealers; i++)
                {
                    ActiveRevealers[i].SetCachedRayDistance();
                }
            }

            //material.DisableKeyword("BLEED");
            //if (AllowBleeding)
            //    material.EnableKeyword("BLEED");
            material.SetInt("BLEED", 0);
            if (AllowBleeding)
                material.SetInt("BLEED", 1);

            material.SetColor(materialColorID, UnknownColor);   

            material.SetFloat(extraRadiusID, SightExtraAmount);

            material.SetFloat(Shader.PropertyToID("_edgeSoftenDistance"), EdgeSoftenDistance);
            material.SetFloat(maxDistanceID, MaxFogDistance);

            #region Pixellation
            //material.SetInt("_pixelate", 0);
            material.DisableKeyword("PIXELATE");
            if (PixelateFog && !WorldSpacePixelate)
            {
                //material.SetInt("_pixelate", 1);
                material.EnableKeyword("PIXELATE");
            }
                

            material.SetInt("_pixelateWS", 0);
            if (PixelateFog && WorldSpacePixelate)
                material.SetInt("_pixelateWS", 1);

            if (PixelateFog)
                material.SetFloat(extraRadiusID, SightExtraAmount + (1f / PixelDensity));
            #endregion

            material.SetFloat("_pixelDensity", PixelDensity);
            material.SetVector("_pixelOffset", PixelGridOffset);

            material.SetInt("_ditherFog", 0);
            if (UseDithering)
                material.SetInt("_ditherFog", 1);
            material.SetFloat("_ditherSize", DitherSize);

            material.SetInt("_invertEffect", 0);
            if (InvertFowEffect)
                material.SetInt("_invertEffect", 1);

            //material.DisableKeyword("FADE_LINEAR");
            //material.DisableKeyword("FADE_SMOOTH");
            //material.DisableKeyword("FADE_SMOOTHER");
            //material.DisableKeyword("FADE_SMOOTHSTEP");
            //material.DisableKeyword("FADE_EXP");
            //switch (FogFade)
            //{
            //    case FogOfWarFadeType.Linear:
            //        material.EnableKeyword("FADE_LINEAR");
            //        break;
            //    case FogOfWarFadeType.Exponential:
            //        material.EnableKeyword("FADE_EXP");
            //        material.SetFloat(fadePowerID, FogFadePower);
            //        break;
            //    case FogOfWarFadeType.Smooth:
            //        material.EnableKeyword("FADE_SMOOTH");
            //        break;
            //    case FogOfWarFadeType.Smoother:
            //        material.EnableKeyword("FADE_SMOOTHER");
            //        break;
            //    case FogOfWarFadeType.Smoothstep:
            //        material.EnableKeyword("FADE_SMOOTHSTEP");
            //        break;
            //}
            switch (FogFade)
            {
                case FogOfWarFadeType.Linear:
                    material.SetInt("_fadeType", 0);
                    break;
                case FogOfWarFadeType.Exponential:
                    material.SetInt("_fadeType", 4);
                    material.SetFloat(fadePowerID, FogFadePower);
                    break;
                case FogOfWarFadeType.Smooth:
                    material.SetInt("_fadeType", 1);
                    break;
                case FogOfWarFadeType.Smoother:
                    material.SetInt("_fadeType", 2);
                    break;
                case FogOfWarFadeType.Smoothstep:
                    material.SetInt("_fadeType", 3);
                    break;
            }
            //material.DisableKeyword("BLEND_MAX");
            //material.DisableKeyword("BLEND_ADDITIVE");
            //switch (BlendType)
            //{
            //    case FogOfWarBlendMode.Max:
            //        material.EnableKeyword("BLEND_MAX");
            //        break;
            //    case FogOfWarBlendMode.Addative:
            //        material.EnableKeyword("BLEND_ADDITIVE");
            //        break;
            //}
            material.SetInt("BLEND_MAX", 1);
            switch (BlendType)
            {
                case FogOfWarBlendMode.Max:
                    material.SetInt("BLEND_MAX", 1);
                    break;
                case FogOfWarBlendMode.Additive:
                    material.SetInt("BLEND_MAX", 0);
                    break;
            }

            switch (FogAppearance)
            {
                case FogOfWarAppearance.Solid_Color:
                    break;
                case FogOfWarAppearance.GrayScale:
                    material.SetFloat(saturationStrengthID, SaturationStrength);
                    break;
                case FogOfWarAppearance.Blur:
                    material.SetFloat(blurStrengthID, BlurStrength);
                    material.SetFloat(blurPixelOffsetMinID, Screen.height * (BlurDistanceScreenPercentMin / 100));
                    material.SetFloat(blurPixelOffsetMaxID, Screen.height * (BlurDistanceScreenPercentMax / 100));
                    material.SetInt(blurSamplesID, BlurSamples);
                    material.SetFloat(blurPeriodID, (2 * Mathf.PI) / BlurSamples);    //TAU = 2 * PI
                    break;
                case FogOfWarAppearance.Texture_Sample:
                    material.SetTexture(fowTetureID, FogTexture);
                    material.SetInt("_skipTriplanar", 0);
                    if (!UseTriplanar)
                    {
                        material.SetInt("_skipTriplanar", 1);
                        material.SetVector("_fowAxis", (Vector3)UpVector);
                    }
                    material.SetVector(fowTilingID, FogTextureTiling);
                    material.SetVector(fowSpeedID, FogScrollSpeed);
                    break;
                case FogOfWarAppearance.Outline:
                    material.SetFloat("lineThickness", OutlineThickness);
                    break;
            }


            material.DisableKeyword("SAMPLE_REALTIME");
            if (FOWSamplingMode == FogSampleMode.Pixel_Perfect || FOWSamplingMode == FogSampleMode.Both)
                material.EnableKeyword("SAMPLE_REALTIME");

            material.DisableKeyword("SAMPLE_TEXTURE");
            material.DisableKeyword("USE_TEXTURE_BLUR");
            if (FOWSamplingMode == FogSampleMode.Texture || FOWSamplingMode == FogSampleMode.Both)
            {
                material.SetTexture("_FowRT", FOW_RT);
                material.EnableKeyword("SAMPLE_TEXTURE");
                
                if (UseConstantBlur)
                {
                    material.EnableKeyword("USE_TEXTURE_BLUR");
                    material.SetFloat("_Sample_Blur_Quality", ConstantTextureBlurQuality);
                    material.SetFloat("_Sample_Blur_Amount", ConstantTextureBlurAmount);
                }
            }

            if (material == FowTextureMaterial)
            {
                //material.SetTexture("_FowRT", FOW_RT);
                //material.SetTexture("_FowRT", FogTexture);
                material.SetFloat("_fadeOutSpeed", RevealerFadeOut ? RevealerFadeOutSpeed : 9999999);
                material.SetFloat("_fadeInSpeed", RevealerFadeIn ? RevealerFadeInSpeed : 9999999);
                material.SetFloat("_maxRegrowAmount", MaxFogRegrowAmount);
                material.EnableKeyword("SAMPLE_REALTIME");
                material.DisableKeyword("SAMPLE_TEXTURE");
                material.DisableKeyword("USE_REGROW");
                if (UseRegrow)
                {
                    material.EnableKeyword("USE_REGROW");
                }
            }

            material.DisableKeyword("USE_WORLD_BOUNDS");
            if (UseRegrow)
                material.EnableKeyword("USE_WORLD_BOUNDS");

            //material.DisableKeyword("USE_WORLD_BOUNDS");
            //if (UseWorldBounds)
            //    material.EnableKeyword("USE_WORLD_BOUNDS");
            material.SetFloat("_worldBoundsInfluence", 0);
            if (UseWorldBounds)
            {
                material.SetFloat("_worldBoundsSoftenDistance", WorldBoundsSoftenDistance);
                material.SetFloat("_worldBoundsInfluence", WorldBoundsInfluence);
            }

            SetMaterialBounds(material);
        }

        public void EnableKeywordOnAllMaterials(string keyword, bool enabled)
        {
            if (!Application.isPlaying)
                return;

            EnableKeywordOnMaterial(FogOfWarMaterial, keyword, enabled);
            if (FowTextureMaterial != null)
                EnableKeywordOnMaterial(FowTextureMaterial, keyword, enabled);

            foreach (PartialHider hider in PartialHiders)
                EnableKeywordOnMaterial(hider.HiderMaterial, keyword, enabled);

            //SetMaterialBounds();
        }

        public void EnableKeywordOnMaterial(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        public const RenderTextureFormat renderTextureFormat = RenderTextureFormat.RHalf;
        public const TextureFormat saveTextureFormat = TextureFormat.RHalf;
        public void InitFOWRT()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            var tmp = RenderTexture.active;
            
            FOW_RT = new RenderTexture(FowResX, FowResY, 0, renderTextureFormat, RenderTextureReadWrite.Linear);
            //Debug.Log(FOW_RT.filterMode);
            //Debug.Log(FOW_RT.antiAliasing);
            //Debug.Log(FOW_RT.anisoLevel);
            //FOW_RT.antiAliasing = FowTextureMsaa;
            //FOW_RT.filterMode = FilterMode.Trilinear;
            //FOW_RT.anisoLevel = 9;

            FOW_RT.filterMode = FilterMode.Bilinear;
            FOW_RT.anisoLevel = 1;
            FOW_RT.useMipMap = false;
            FOW_RT.antiAliasing = 1;
            FOW_RT.Create();
            RenderTexture.active = FOW_RT;
            //GL.Begin(GL.TRIANGLES);
            GL.Clear(true, true, new Color(1 - InitialFogExplorationValue, 0, 0, 1 - InitialFogExplorationValue));

            if (UseRegrow)
            {
                FOW_TEMP_RT = new RenderTexture(FOW_RT);
                FOW_TEMP_RT.Create();
            }

            RenderTexture.active = tmp;
        }

        public RenderTexture GetFOWRT()
        {
            return FOW_RT;
        }

        [Obsolete("Please use ClearFowTexture() instead")]
        public void ClearRegrowTexture()
        {
            ClearFowTexture();
        }

        public void ClearFowTexture()
        {
            var tmp = RenderTexture.active;

            RenderTexture.active = FOW_RT;
            GL.Begin(GL.TRIANGLES);
            GL.Clear(true, true, new Color(0, 0, 0, 1 - InitialFogExplorationValue));
            GL.End();
            RenderTexture.active = FOW_TEMP_RT;
            GL.Begin(GL.TRIANGLES);
            GL.Clear(true, true, new Color(0, 0, 0, 1 - InitialFogExplorationValue));
            GL.End();

            RenderTexture.active = tmp;
        }

        #endregion

        #region All Bounds Stuff

        public void UpdateWorldBounds(Vector3 center, Vector3 extent)
        {
            WorldBounds.center = center;
            WorldBounds.extents = extent;
            FowBoundsUpdated();
        }

        public void UpdateWorldBounds(Bounds newBounds)
        {
            WorldBounds = newBounds;
            FowBoundsUpdated();
        }

        public static Vector4 CachedFowShaderBounds;
        private void FowBoundsUpdated()
        {
            CachedFowShaderBounds = GetBoundsVectorForShader();
            SetAllMaterialBounds();
        }

        private void SetAllMaterialBounds()
        {
            if (FogOfWarMaterial != null)
                SetMaterialBounds(FogOfWarMaterial);

            if (FowTextureMaterial != null)
                SetMaterialBounds(FowTextureMaterial);
        }

        void SetMaterialBounds(Material mat)
        {
            //if (UseWorldBounds && FogOfWarMaterial != null)
            if (mat != null)
                mat.SetVector("_worldBounds", CachedFowShaderBounds);
        }

        public Vector4 GetBoundsVectorForShader()
        {
            if (is2D)
                return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.y, WorldBounds.center.y);

            switch(GamePlaneOrientation)
            {
                case GamePlane.XZ: return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.z, WorldBounds.center.z);
                case GamePlane.XY: return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.y, WorldBounds.center.y);
                case GamePlane.ZY: return new Vector4(WorldBounds.size.z, WorldBounds.center.z, WorldBounds.size.z, WorldBounds.center.z);
            }

            return new Vector4(WorldBounds.size.x, WorldBounds.center.x, WorldBounds.size.z, WorldBounds.center.z);
        }

        /// <summary>
        /// Gets the world position of the provided point on the FOW plane
        /// </summary>
        public Vector2 GetFowBoundsPositionFromWorldPosition(Vector3 WorldPosition)
        {
            if (is2D)
                return new Vector2(WorldPosition.x, WorldPosition.y);

            switch (GamePlaneOrientation)
            {
                case GamePlane.XZ: return new Vector2(WorldPosition.x, WorldPosition.z);
                case GamePlane.XY: return new Vector2(WorldPosition.x, WorldPosition.y);
                case GamePlane.ZY: return new Vector2(WorldPosition.z, WorldPosition.y);
            }

            return new Vector2(WorldPosition.x, WorldPosition.z);
        }

        #endregion

        #region Revealer Tracking

        void SetNumRevealers()
        {
            //todo: switch to global int
            if (FogOfWarMaterial != null)
                SetNumRevealers(FogOfWarMaterial);
            if (FowTextureMaterial != null)
                SetNumRevealers(FowTextureMaterial);
            foreach (PartialHider hider in PartialHiders)
                SetNumRevealers(hider.HiderMaterial);
        }

        public void SetNumRevealers(Material material)
        {
            material.SetInt(numRevealersID, NumActiveRevealers);
        }
        
        public int RegisterRevealer(FogOfWarRevealer newRevealer)
        {
#if UNITY_EDITOR
            //RegisterRevealersProfileMarker.Begin();
#endif
            int emptySlotID = NumActiveRevealers;

            NumActiveRevealers++;
            if (!newRevealer.CurrentlyStaticRevealer)
                numDynamicRevealers++;
            SetNumRevealers();
            
            ActiveRevealers[emptySlotID] = newRevealer;
            newRevealer.RevealerArrayPosition = emptySlotID;

            int newID = emptySlotID;
            if (numDeregisteredRevealers > 0)
            {
                numDeregisteredRevealers--;
                newID = DeregisteredRevealerIDs[0];
                DeregisteredRevealerIDs.RemoveAt(0);
            }

            indiciesDataToSet[0] = newID;
            ActiveRevealerIndicesBuffer.SetData(indiciesDataToSet, 0, emptySlotID, 1);

            //_circleIndicesArray = indicesBuffer.BeginWrite<int>(numCircles - 1, 1);
            //_circleIndicesArray[0] = newID;

            //indicesBuffer.EndWrite<int>(1);

#if UNITY_EDITOR
            //RegisterRevealersProfileMarker.End();
#endif
            UnsortedRevealers[newID] = newRevealer;
            return newID;
        }

        public void DeRegisterRevealer(FogOfWarRevealer toRemove)
        {
#if UNITY_EDITOR
            //DeRegisterRevealersProfileMarker.Begin();
#endif
            int index = toRemove.RevealerArrayPosition;

            DeregisteredRevealerIDs.Add(toRemove.RevealerGPUDataPosition);
            numDeregisteredRevealers++;

            NumActiveRevealers--;
            if (!toRemove.CurrentlyStaticRevealer)
                numDynamicRevealers--;

            FogOfWarRevealer toSwap = ActiveRevealers[NumActiveRevealers]; //the last revealer in the buffer
            if (toSwap.RevealerArrayPosition != index) //put the LAST active revealer in this slot
            {
                //swap the array position
                ActiveRevealers[index] = toSwap;
                toSwap.RevealerArrayPosition = index;

                //notify the gpu about the swap
                indiciesDataToSet[0] = toSwap.RevealerGPUDataPosition;
                ActiveRevealerIndicesBuffer.SetData(indiciesDataToSet, 0, index, 1);
            }

            SetNumRevealers();

#if UNITY_EDITOR
            //DeRegisterRevealersProfileMarker.End();
#endif
        }

        public int RegisterHider(FogOfWarHider newHider)
        {
#if UNITY_EDITOR
            //RegisterHiderProfileMarker.Begin();
#endif
            int emptySlotID = NumActiveHiders;

            if (ActiveHiders.Length == emptySlotID)
            {
                Array.Resize(ref ActiveHiders, ActiveHiders.Length * 2);
                Array.Resize(ref UnsortedHiders, UnsortedHiders.Length * 2);
            }

            NumActiveHiders++;

            ActiveHiders[emptySlotID] = newHider;
            newHider.HiderArrayPosition = emptySlotID;

            int newID = emptySlotID;
            if (numDeregisteredHiders > 0)
            {
                numDeregisteredHiders--;
                newID = DeregisteredHiderIDs[0];
                DeregisteredHiderIDs.RemoveAt(0);
            }

            UnsortedHiders[newID] = newHider;
#if UNITY_EDITOR
            //RegisterHiderProfileMarker.End();
#endif
            return newID;
        }

        public void DeRegisterHider(FogOfWarHider toRemove)
        {
#if UNITY_EDITOR
            //DeRegisterHiderProfileMarker.Begin();
#endif
            int index = toRemove.HiderArrayPosition;

            DeregisteredHiderIDs.Add(toRemove.HiderPermanantID);
            numDeregisteredHiders++;

            NumActiveHiders--;

            FogOfWarHider toSwap = ActiveHiders[NumActiveHiders]; //the last hider in the buffer
            if (toSwap.HiderArrayPosition != index) //put the LAST active hider in this slot
            {
                //swap the array position
                ActiveHiders[index] = toSwap;
                toSwap.HiderArrayPosition = index;
            }

#if UNITY_EDITOR
            //DeRegisterHiderProfileMarker.End();
#endif
        }

        #endregion

        #region Shader Data Upload

        private RevealerInfoStruct[] _revealerInfoToSet = new RevealerInfoStruct[1];
        public void UpdateRevealerInfo(int id, RevealerInfoStruct info)
        {
            _revealerInfoToSet[0] = info;
            RevealerInfoBuffer.SetData(_revealerInfoToSet, 0, id, 1);
        }

        //private JobHandle setAnglesBuffersJobHandle;
        //private SetAnglesBuffersJob setAnglesBuffersJob;
        //private NativeArray<ConeEdgeStruct> AnglesNativeArray;    //was used when using computebuffer.beginwrite. will be used again when unity fixes a bug internally
        //private NativeArray<int> _circleIndicesArray;
        //private NativeArray<CircleStruct> _circleArray;
        //private NativeArray<ConeEdgeStruct> _angleArray;
        private GpuSightSegment[] SightSegmentsUploadData;
        private RevealerDataStruct[] _revealerDataToSet = new RevealerDataStruct[1];
        public void UpdateRevealerData(int gpuPositionId, in RevealerDataStruct data, int numHits, float2[] directions, float[] distances)
        {
#if UNITY_EDITOR
            UploadToGpuProfileMarker.Begin();
#endif

            UpdateRevealerDataLegacy(gpuPositionId, data, numHits, directions, distances);
            //todo: replace UpdateRevealerDataLegacy with a batch updater (might require compute shader)

#if UNITY_EDITOR
            UploadToGpuProfileMarker.End();
#endif
        }

        void UpdateRevealerDataLegacy(int gpuPositionId, in RevealerDataStruct data, int numHits, float2[] directions, float[] distances)
        {
            //setAnglesBuffersJobHandle.Complete();

            _revealerDataToSet[0] = data;
            RevealerDataBuffer.SetData(_revealerDataToSet, 0, gpuPositionId, 1);
            //_circleArray = circleBuffer.BeginWrite<CircleStruct>(gpuPositionId, 1);
            //_circleArray[0] = data;
            //circleBuffer.EndWrite<CircleStruct>(1);

            if (numHits == 0)
                return;
            else if (numHits > MaxPossibleSegmentsPerRevealer)
            {
                Debug.LogError($"the revealer is trying to register {numHits} segments. this is more than was set by maxPossibleSegmentsPerRevealer");
                return;
            }

            for (int i = 0; i < numHits; i++)
            {
                ref var segment = ref SightSegmentsUploadData[i];
                //segment.angle = radii[i];
                segment.direction = directions[i];
                segment.length = distances[i];
            }

            AnglesBuffer.SetData(SightSegmentsUploadData, 0, gpuPositionId * MaxPossibleSegmentsPerRevealer, numHits);
            //the following lines of code should work in theory, however due to a unity bug, are going to be put on hold for a little bit.
            //_angleArray = anglesBuffer.BeginWrite<ConeEdgeStruct>(gpuPositionId * maxPossibleSegmentsPerRevealer, radii.Length);
            //setAnglesBuffersJob.AnglesArray = _angleArray;
            //setAnglesBuffersJob.Angles = AnglesNativeArray;
            //setAnglesBuffersJobHandle = setAnglesBuffersJob.Schedule(radii.Length, 128);
            //setAnglesBuffersJobHandle.Complete();
            //anglesBuffer.EndWrite<ConeEdgeStruct>(radii.Length);
        }

        void UpdateRevealerDataCompute()
        {
            //_stagingSegmentTotal += numHits;
            //_stagingCount++;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SetAnglesBuffersJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<GpuSightSegment> Angles;
            [WriteOnly]
            public NativeArray<GpuSightSegment> AnglesArray;

            public void Execute(int index)
            {
                AnglesArray[index] = Angles[index];
            }
        }

        public static void OnPreRenderFog()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            if (SparseRevealerGrid.SpatialAccelerationActive && instance.FOWSamplingMode == FogSampleMode.Pixel_Perfect)
                SparseRevealerGrid.FlattenAndUpload();
        }

        #endregion

        /// <summary>
        /// Set the Global strength of FOW shaders. Range: 0-1
        /// </summary>
        public static void SetFowEffectStrength(float strength)
        {
            Shader.SetGlobalFloat(FowEffectStrengthID, strength);
        }

        /// <summary>
        /// Translates world position to FOW texture UV
        /// </summary>
        public static float2 GetFowTextureUVFromWorldPosition(Vector3 WorldPosition)
        {
            var bounds = FogOfWarWorld.CachedFowShaderBounds;
            float2 Position = instance.GetFowBoundsPositionFromWorldPosition(WorldPosition);
            float2 uv = new Vector2((((Position.x - bounds.y) + (bounds.x / 2)) / bounds.x),
                 (((Position.y - bounds.w) + (bounds.z / 2)) / bounds.z));

            return uv;
        }

        /// <summary>
        /// Test if provided point is currently visible.
        /// </summary>
        public static bool SampleFogTextureAtPoint(Vector3 WorldPosition)
        {
            float color = SampleFogTextureColorAtPoint(WorldPosition);

            if (color > .5f)
                return true;

            return false;
        }

        /// <summary>
        /// Samples the fog texture opacity at the given world position
        /// </summary>
        public static float SampleFogTextureColorAtPoint(Vector3 WorldPosition)
        {
            Vector2 uv = GetFowTextureUVFromWorldPosition(WorldPosition);

            if (instance._asyncFogTextureReader != null && instance._asyncFogTextureReader.HasData && (instance.AsyncReadbackFogDataToCpu || NumActiveHiders != 0))
                return 1 - instance._asyncFogTextureReader.SampleAsyncData(uv);

            //Debug.Log("taking slow path");
            Color color = SamplePixelSlow(FOW_RT, uv);

            //white = see, black = not see
            return 1 - color.r;
        }

        static Texture2D sampleTex;
        private static Color SamplePixelSlow(RenderTexture rt, Vector2 uv)
        {
            if (rt == null) return Color.magenta;

            int x = Mathf.Clamp((int)(uv.x * rt.width), 0, rt.width - 1);
            int y = Mathf.Clamp((int)(uv.y * rt.height), 0, rt.height - 1);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            if (sampleTex == null)
                sampleTex = new Texture2D(1, 1, saveTextureFormat, false, true);
            sampleTex.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            sampleTex.Apply(false, false);

            Color c = sampleTex.GetPixel(0, 0);

            RenderTexture.active = previous;

            return c;
        }

        /// <summary>
        /// Test if provided point is currently visible.
        /// </summary>
        public static bool TestPointVisibility(Vector3 point)
        {
            if (instance.UseSpatialAcceleration)
            {
                Vector2 projectedPos = FogOfWarRevealer3D.Projection.Project(point);
                int hash = SparseRevealerGrid.GetCellHash(projectedPos);
                for (int i = 0; i < SparseRevealerGrid.RevealerBucketCounts[hash]; i++)
                {
                    if (UnsortedRevealers[SparseRevealerGrid.RevealerBuckets[hash][i]].TestPoint(point))
                        return true;
                }
            }
            else
            {
                for (int i = 0; i < NumActiveRevealers; i++)
                {
                    if (ActiveRevealers[i].TestPoint(point))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Change the fog appearence type at runtime
        /// </summary>
        public void SetFowAppearance(FogOfWarAppearance AppearanceMode)
        {
            FogAppearance = AppearanceMode;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif

            enabled = false;
            enabled = true;
        }

        public FogOfWarAppearance GetFowAppearance()
        {
            return FogAppearance;
        }

        /// <summary>
        /// Retuns a byte array that you can save to a file
        /// </summary>
        public byte[] GetFowTextureSaveData()
        {
            var tex = new Texture2D(FOW_RT.width, FOW_RT.height, saveTextureFormat, mipChain: false, linear: true);

            var tmp = RenderTexture.active;

            RenderTexture.active = FOW_RT;
            tex.ReadPixels(new Rect(0, 0, FOW_RT.width, FOW_RT.height), 0, 0, false);
            tex.Apply(false, false);

            RenderTexture.active = tmp;

            Destroy(tex);

            return ImageConversion.EncodeToPNG(tex);
        }

        /// <summary>
        /// Loads the FOW exploration data from a byte array created with GetFowTextureSaveData
        /// </summary>
        public void LoadFowTextureData(byte[] save)
        {
            ClearFowTexture();

            Texture2D temp = new Texture2D(1, 1, saveTextureFormat, mipChain: false, linear: true);
            temp.LoadImage(save);

            Graphics.Blit(temp, FOW_RT);
        }
    }

    //this class revealers hiders based off the FOW texture, instead of using revealers directly
    public sealed class AsyncFogTextureReader : IDisposable
    {
        public bool HasData;
        public HiderRevealer HiderSeeker;
        public float HiderSeeingThreshold = .5f;

        NativeArray<half> _front;
        NativeArray<half> _back;
        int _w, _h;
        bool _requestPending;
        AsyncGPUReadbackRequest _request;

        public AsyncFogTextureReader()
        {
            HiderSeeker = new HiderRevealer();
        }

        public void Update(RenderTexture rt)
        {
            if (rt == null) return;

            if (!_front.IsCreated || _w != rt.width || _h != rt.height)
            {
                if (_requestPending)
                {
                    _request.WaitForCompletion();
                    _requestPending = false;
                }
                Resize(rt.width, rt.height);
            }

            if (_requestPending && _request.done)
            {
                _requestPending = false;

                if (!_request.hasError)
                {
                    (_front, _back) = (_back, _front); //swap front and back
                    HasData = true;
                }
            }

            if (!_requestPending)
            {
                //_request = AsyncGPUReadback.RequestIntoNativeArray(ref _back, rt, 0);
                _request = AsyncGPUReadback.RequestIntoNativeArray(
                    ref _back,
                    rt,
                    0,
                    FogOfWarWorld.saveTextureFormat);
                _requestPending = true;
            }
        }

        public void SeekHiders()
        {
            if (FogOfWarWorld.NumActiveHiders == 0)
                return;

            if (!HasData) return;

            for (int i = 0; i < FogOfWarWorld.NumActiveHiders; i++)
            {
                FogOfWarHider hider = FogOfWarWorld.ActiveHiders[i];
                bool seen = CanSeeHider(hider);
                HiderSeeker.ProcessSeen(hider, seen);
            }
        }

        bool CanSeeHider(FogOfWarHider hider)
        {
            var bounds = FogOfWarWorld.CachedFowShaderBounds;

            for (int i = 0; i < hider.SamplePoints.Length; i++)
            {
                float3 worldPos = hider.SamplePoints[i].position;
                float2 uv = FogOfWarWorld.GetFowTextureUVFromWorldPosition(worldPos);

                float sample = SampleAsyncData(uv);
                if (sample < HiderSeeingThreshold)
                    return true;
            }

            return false;
        }

        public half SampleAsyncData(float2 uv)
        {
            //uv.y = 1 - uv.y;

            int x = Mathf.Clamp((int)(uv.x * _w), 0, _w - 1);
            int y = Mathf.Clamp((int)(uv.y * _h), 0, _h - 1);

            return _front[y * _w + x];
        }

        void Resize(int w, int h)
        {
            if (_front.IsCreated) _front.Dispose();
            if (_back.IsCreated) _back.Dispose();

            _w = w;
            _h = h;
            int size = w * h;
            _front = new NativeArray<half>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _back = new NativeArray<half>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            HasData = false;
        }

        public void UnseeAllHiders()
        {
            HiderSeeker.ClearRevealedList();
        }

        public void Dispose()
        {
            HiderSeeker?.ClearRevealedList();
            if (_requestPending)
            {
                _request.WaitForCompletion();
                _requestPending = false;
            }

            if (_front.IsCreated) _front.Dispose();
            if (_back.IsCreated) _back.Dispose();
            HasData = false;
        }
    }

    public static class SparseRevealerGrid
    {
        public static bool SpatialAccelerationActive = false;
        public static List<int>[] RevealerBuckets; //each bucket has a list of revealer ids
        public static int[] RevealerBucketCounts;  //cached bucket counts
        public static List<int>[] HiderBuckets;
        public static int[] HiderBucketCounts;

        public static bool Dirty;

        //static List<int> _activeBuckets = new List<int>();
        static int _totalEntries = 0;
        private static HashSet<int> _tempHashes = new HashSet<int>();

        static int _tableSize = 512;
        static int _cellSize = 32;

        static ComputeBuffer _gridRangesBuffer;
        static ComputeBuffer _revealerGridIdsBuffer;

        static int2[] _ranges;
        static int[] _revealerGridIds;
        static int _maxGridIds;

        public static void Initialize(int tableSize, int cellSize)
        {
            SpatialAccelerationActive = true;
            _tableSize = tableSize;
            _cellSize = cellSize;

            RevealerBuckets = new List<int>[_tableSize];
            RevealerBucketCounts = new int[_tableSize];

            HiderBuckets = new List<int>[_tableSize];
            HiderBucketCounts = new int[_tableSize];
            
            for (int i = 0; i < _tableSize; i++)
            {
                RevealerBuckets[i] = new List<int>();
                HiderBuckets[i] = new List<int>();
            }

            _maxGridIds = FogOfWarWorld.instance.MaxPossibleRevealers * 4;  //it will automatically resize if needed
            _ranges = new int2[_tableSize];
            _revealerGridIds = new int[_maxGridIds];

            _gridRangesBuffer = new ComputeBuffer(_tableSize, sizeof(int) * 2);
            _revealerGridIdsBuffer = new ComputeBuffer(_maxGridIds, sizeof(int));

            FogOfWarWorld.instance.BindSpatialHashComputeBuffersToAllMaterials();
        }

        public static void Cleanup()
        {
            SpatialAccelerationActive = false;

            if (FogOfWarWorld.ActiveRevealers != null)
            {
                for (int i = 0; i < FogOfWarWorld.NumActiveRevealers; i++)
                    FogOfWarWorld.ActiveRevealers[i].SpatialHashBuckets.Clear();
            }
            if (FogOfWarWorld.ActiveHiders != null)
            {
                for (int i = 0; i < FogOfWarWorld.NumActiveHiders; i++)
                    FogOfWarWorld.ActiveHiders[i].SpatialHashBuckets.Clear();
            }

            _gridRangesBuffer?.Dispose();
            _revealerGridIdsBuffer?.Dispose();
            _gridRangesBuffer = null;
            _revealerGridIdsBuffer = null;
            _totalEntries = 0;
        }

        public static int2 GetCell(float2 position)
        {
            return new int2(
                (int)math.floor(position.x / _cellSize),
                (int)math.floor(position.y / _cellSize)
            );
        }

        public static int GetCellHash(int2 cell)
        {
            uint h = (uint)cell.x * 73856093u ^ (uint)cell.y * 19349663u;
            return (int)(h % (uint)_tableSize);
        }

        public static int GetCellHash(float2 position)
        {
            int2 cell = GetCell(position);

            return GetCellHash(cell);
        }

        private static void SwapRemove(List<int> list, int item)    //order doesnt matter for revealer buckets and this is slightly faster than regular remove
        {
            int index = list.IndexOf(item);
            if (index < 0) return;

            list[index] = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
        }

        public static void UpdateRevealerBuckets(FogOfWarRevealer revealer, float2 position)
        {
            if (!SpatialAccelerationActive)
                return;

            //add revealer to new buckets based off revealer sight angle, radius, position, and facing direction (could also use first/last sight angle direction idk)
            int2 minCell = GetCell(position - revealer.TotalRevealerRadius);
            int2 maxCell = GetCell(position + revealer.TotalRevealerRadius);

            if (revealer.MinBucket.x == minCell.x && revealer.MinBucket.y == minCell.y &&
                revealer.MaxBucket.x == maxCell.x && revealer.MaxBucket.y == maxCell.y)
                return;

            revealer.MinBucket = minCell;
            revealer.MaxBucket = maxCell;

            for (int i = 0; i < revealer.SpatialHashBuckets.Count; i++)
            {
                int hash = revealer.SpatialHashBuckets[i];
                SwapRemove(RevealerBuckets[hash], revealer.RevealerGPUDataPosition);
                //RevealerBuckets[hash].Remove(revealer.RevealerGPUDataPosition);
                RevealerBucketCounts[hash]--;
                _totalEntries--;
                //if (BucketCounts[hash] == 0)
                //    _activeBuckets.Remove(hash);
            }
            revealer.SpatialHashBuckets.Clear();
            _tempHashes.Clear();

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    int hash = GetCellHash(new int2(x, y));

                    if (!_tempHashes.Add(hash))
                        continue;

                    //if (BucketCounts[hash] == 0)
                    //    _activeBuckets.Add(hash);

                    RevealerBuckets[hash].Add(revealer.RevealerGPUDataPosition);
                    RevealerBucketCounts[hash]++;
                    _totalEntries++;
                    revealer.SpatialHashBuckets.Add(hash);
                }
            }
            Dirty = true;
        }

        public static void RemoveRevealer(FogOfWarRevealer revealer)
        {
            for (int i = 0; i < revealer.SpatialHashBuckets.Count; i++)
            {
                int hash = revealer.SpatialHashBuckets[i];
                //RevealerBuckets[hash].Remove(revealer.RevealerGPUDataPosition);
                SwapRemove(RevealerBuckets[hash], revealer.RevealerGPUDataPosition);
                RevealerBucketCounts[hash]--;
            }
            revealer.SpatialHashBuckets.Clear();
            Dirty = true;
        }

        public static void UpdatHiderBuckets(FogOfWarHider hider, float2 position)
        {
            if (!SpatialAccelerationActive)
                return;

            //add hider to new buckets based off his SAMPLE POSITIONS
            int2 minCell = GetCell(position - hider.MaxSamplePointLocalPosition);
            int2 maxCell = GetCell(position + hider.MaxSamplePointLocalPosition);

            if (hider.MinBucket.x == minCell.x && hider.MinBucket.y == minCell.y &&
                hider.MaxBucket.x == maxCell.x && hider.MaxBucket.y == maxCell.y)
                return;

            hider.MinBucket = minCell;
            hider.MaxBucket = maxCell;


            for (int i = 0; i < hider.SpatialHashBuckets.Count; i++)
            {
                int hash = hider.SpatialHashBuckets[i];
                SwapRemove(HiderBuckets[hash], hider.HiderPermanantID);
                HiderBucketCounts[hash]--;
                _totalEntries--;
            }
            hider.SpatialHashBuckets.Clear();
            _tempHashes.Clear();

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    int hash = GetCellHash(new int2(x, y));

                    if (!_tempHashes.Add(hash))
                        continue;

                    HiderBuckets[hash].Add(hider.HiderPermanantID);
                    HiderBucketCounts[hash]++;
                    _totalEntries++;
                    hider.SpatialHashBuckets.Add(hash);
                }
            }

            return;
        }

        public static void RemoveHider(FogOfWarHider hider)
        {
            for (int i = 0; i < hider.SpatialHashBuckets.Count; i++)
            {
                int hash = hider.SpatialHashBuckets[i];
                //HiderBuckets[hash].Remove(hider.HiderPermanantID);
                SwapRemove(HiderBuckets[hash], hider.HiderPermanantID);
                HiderBucketCounts[hash]--;
            }
            hider.SpatialHashBuckets.Clear();
        }

#if UNITY_EDITOR
        static readonly ProfilerMarker FlattenProfileMarker = new ProfilerMarker("Spatial Hash Flatten + Upload");
#endif
        public static void FlattenAndUpload()
        {
            if (!Dirty)
                return;

#if UNITY_EDITOR
            FlattenProfileMarker.Begin();
#endif
            Dirty = false;

            #region check if we need to resize the grid id buffer

            //int totalEntries = 0;
            //for (int i = 0; i < _activeBuckets.Count; i++)
            //    totalEntries += BucketCounts[_activeBuckets[i]];

            if (_totalEntries > _maxGridIds)
            {
                _maxGridIds = _totalEntries * 2;
                _revealerGridIds = new int[_maxGridIds];
                _revealerGridIdsBuffer?.Dispose();
                _revealerGridIdsBuffer = new ComputeBuffer(_maxGridIds, sizeof(int));

                FogOfWarWorld.instance.BindSpatialHashComputeBuffersToAllMaterials();   //rebind after resizing
            }

            #endregion

            // Flatten
            int writeIndex = 0;
            for (int i = 0; i < _tableSize; i++)
            {
                int count = RevealerBucketCounts[i];
                _ranges[i] = new int2(writeIndex, writeIndex + count);

                var bucket = RevealerBuckets[i];
                //if (count > 1)
                //{
                //    //this can help improve cache locality, which can make the shader code a bit faster on the gpu. i need to test this more so ill leave it commented for now
                //    //bucket.Sort();
                //    InsertionSort(bucket);
                //}

                for (int j = 0; j < count; j++)
                    _revealerGridIds[writeIndex++] = bucket[j];
            }

            // Upload
            _gridRangesBuffer.SetData(_ranges);
            if (_totalEntries > 0)
                _revealerGridIdsBuffer.SetData(_revealerGridIds, 0, 0, _totalEntries);

#if UNITY_EDITOR
            FlattenProfileMarker.End();
#endif
        }

        private static void InsertionSort(List<int> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                int key = list[i];
                int j = i - 1;

                while (j >= 0 && list[j] > key)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }

        public static bool CheckIntersection(int2 minBucket1, int2 maxBucket1, int2 minBucket2, int2 maxBucket2)
        {
            return math.all(maxBucket1 >= minBucket2) & math.all(minBucket1 <= maxBucket2);
        }

        public static void BindPropertiesToMaterial(Material material)
        {
            material.SetBuffer("_GridRanges", _gridRangesBuffer);
            material.SetBuffer("_RevealerGridIds", _revealerGridIdsBuffer);
            material.SetInt("_TableSize", _tableSize);
            material.SetFloat("_CellSize", _cellSize);
        }
    }
}