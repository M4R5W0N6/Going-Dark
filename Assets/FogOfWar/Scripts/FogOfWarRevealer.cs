using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FOW
{
    public abstract class FogOfWarRevealer : MonoBehaviour
    {
        [Header("Customization Variables")]
        public float viewRadius = 15;

        [Range(0, 360)]
        public float viewAngle = 360;

        public float unobscuredRadius = 1f;

        public bool addCorners;

        public bool revealHidersInFadeOutZone = true;

        [Tooltip("how high above this object should the sight be calculated from")]
        public float eyeOffset = 0;
        public float visionHeight = 3;

        [Header("Technical Variables")]
        public LayerMask obstacleMask;
        public float RaycastResolution = .5f;
        [Range(1, 30)]
        [Tooltip("Higher values will lead to more accurate edge detection, especially at higher distances. however, this will also result in more raycasts.")]
        public int maxEdgeResolveIterations = 10;

        [Range(0, 10)]
        public int numExtraIterations = 3;

        [Range(0, 5)]
        public int numExtraRaysOnIteration = 4;

        [HideInInspector]
        public int fogOfWarID;
        [HideInInspector]
        public int indexID;

        //local variables
        protected List<ViewCastInfo> viewPoints = new List<ViewCastInfo>();

        [Header("debug, you shouldnt have to mess with this")]
        [Range(.001f, 1)]
        [Tooltip("Lower values will lead to more accurate edge detection, especially at higher distances. however, this will also result in more raycasts.")]
        public float maxAcceptableEdgeAngleDifference = .001f;
        public float edgeDstThreshold = 0.1f;
        public float doubleHitMaxDelta = 0.1f;
        public float doubleHitMaxAngleDelta = 15;
#if UNITY_EDITOR
        public bool logNumRaycasts = false;
        public bool debugMode = false;
        protected int numRayCasts;
        public float drawRayNoise = 0;
        public bool drawExtraCastLines;
        public bool drawIteritiveLines;
#endif

        public List<FogOfWarHider> hidersSeen = new List<FogOfWarHider>();

        public struct ViewCastInfo
        {
            public bool hit;
            public Vector3 point;
            public float dst;
            public float angle;
            public Vector3 normal;
            public Vector3 direction;

            public ViewCastInfo(bool _hit, Vector3 _point, float _dst, float _angle, Vector3 _normal, Vector3 dir)
            {
                hit = _hit;
                point = _point;
                dst = _dst;
                angle = _angle;
                normal = _normal;
                direction = dir;
            }
        }

        public struct EdgeInfo
        {
            public ViewCastInfo minViewCast;
            public ViewCastInfo maxViewCast;
            public bool shouldUse;

            public EdgeInfo(ViewCastInfo _pointA, ViewCastInfo _pointB, bool _shouldUse)
            {
                minViewCast = _pointA;
                maxViewCast = _pointB;
                shouldUse = _shouldUse;
            }
        }

        public void registerRevealer()
        {
            _RegisterRevealer();
        }
        protected abstract void _RegisterRevealer();

        public void CalculateLineOfSight()
        {
            _CalculateLineOfSight();
        }
        protected abstract void _CalculateLineOfSight();

        public bool TestPoint(Vector3 point)
        {
            return testPoint(point);
        }
        protected abstract bool testPoint(Vector3 point);
    }
}
