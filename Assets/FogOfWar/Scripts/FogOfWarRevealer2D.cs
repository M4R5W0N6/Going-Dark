using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.Profiling;
#endif

namespace FOW
{
    public class FogOfWarRevealer2D : FogOfWarRevealer
    {
        private void OnEnable()
        {
            _RegisterRevealer();
        }

        private void OnDisable()
        {
            deregisterRevealer();
        }

        bool isRegistered = false;
        protected override void _RegisterRevealer()
        {
            if (FogOfWarWorld.instance == null)
            {
                if (!FogOfWarWorld.revealersToRegister.Contains(this))
                {
                    FogOfWarWorld.revealersToRegister.Add(this);
                }
                return;
            }
            if (isRegistered)
            {
                Debug.Log("Tried to double register revealer");
                return;
            }
            isRegistered = true;
            fogOfWarID = FogOfWarWorld.instance.registerRevealer(this);
            circleStruct = new FogOfWarWorld.CircleStruct();
            _CalculateLineOfSight();
        }

        public void deregisterRevealer()
        {
            if (FogOfWarWorld.instance == null)
            {
                if (FogOfWarWorld.revealersToRegister.Contains(this))
                {
                    FogOfWarWorld.revealersToRegister.Remove(this);
                }
                return;
            }
            if (!isRegistered)
            {
                //Debug.Log("Tried to de-register revealer thats not registered");
                return;
            }
            foreach (FogOfWarHider hider in hidersSeen)
            {
                hider.removeSeer(this);
            }
            hidersSeen.Clear();
            isRegistered = false;
            FogOfWarWorld.instance.deRegisterRevealer(this);
        }

        bool circleIsComplete;
        float stepAngleSize;
        Vector3 expectedNextPoint;
        protected override void _CalculateLineOfSight()
        {

#if UNITY_EDITOR
            Profiler.BeginSample("Revealing Hiders");
#endif
            revealHiders();
#if UNITY_EDITOR
            Profiler.EndSample();
            numRayCasts = 0;
#endif
#if UNITY_EDITOR
            Profiler.BeginSample("Line Of Sight");
#endif
            int stepCount = Mathf.RoundToInt(viewAngle * RaycastResolution);
            stepAngleSize = viewAngle / stepCount;

            ViewCastInfo oldViewCast = new ViewCastInfo();
            circleIsComplete = Mathf.Approximately(viewAngle, 360);
            float firstAng = 0;
            if (!circleIsComplete)
            {
                firstAng = ((-getEuler() + 360 + 90) % 360) - (viewAngle / 2);
            }
            ViewCastInfo firstViewCast = ViewCast(firstAng);

            if (!circleIsComplete)
            {
                viewPoints.Add(firstViewCast);
            }

            float angleC = 180 - (AngleBetweenVector2(-Vector3.Cross(firstViewCast.normal, FogOfWarWorld.upVector), -firstViewCast.direction.normalized) + stepAngleSize);
            float nextDist = (firstViewCast.dst * Mathf.Sin(Mathf.Deg2Rad * stepAngleSize)) / Mathf.Sin(Mathf.Deg2Rad * angleC);
            expectedNextPoint = firstViewCast.point + (-Vector3.Cross(firstViewCast.normal, FogOfWarWorld.upVector) * nextDist);

            oldViewCast = firstViewCast;
            for (int i = 1; i < stepCount; i++)
            {
                float angle = firstViewCast.angle + stepAngleSize * i;
                ViewCastInfo newViewCast = ViewCast(angle);

                determineEdge(oldViewCast, newViewCast);

                angleC = 180 - (Mathf.Abs(AngleBetweenVector2(-Vector3.Cross(newViewCast.normal, FogOfWarWorld.upVector), -newViewCast.direction.normalized)) + stepAngleSize);
                nextDist = (newViewCast.dst * Mathf.Sin(Mathf.Deg2Rad * stepAngleSize)) / Mathf.Sin(Mathf.Deg2Rad * angleC);
                expectedNextPoint = newViewCast.point + (-Vector3.Cross(newViewCast.normal, FogOfWarWorld.upVector) * nextDist);

#if UNITY_EDITOR
                if (debugMode)
                {
                    Vector3 dir = DirFromAngle(angle, true);
                    if (newViewCast.hit)
                        Debug.DrawRay(getEyePos(), dir * (newViewCast.dst), Color.green);
                    else
                        Debug.DrawRay(getEyePos(), dir * (newViewCast.dst), Color.red);
                    Debug.DrawLine(newViewCast.point, expectedNextPoint + FogOfWarWorld.upVector * .1f, Random.ColorHSV());
                }
#endif

                oldViewCast = newViewCast;
            }

            if (circleIsComplete)
            {
                firstViewCast.angle = 360;
                determineEdge(oldViewCast, firstViewCast);
                if (viewPoints.Count == 0)
                {
                    viewPoints.Add(new ViewCastInfo(false, -Vector3.right * viewRadius + getEyePos(), viewRadius, 180, -Vector3.right, -Vector3.right));
                }
                viewPoints.Add(viewPoints[0]);
            }
            else
            {
                viewPoints.Add(oldViewCast);
            }

#if UNITY_EDITOR
            if (logNumRaycasts)
            {
                Debug.Log($"Number of raycasts this update: {numRayCasts}");
            }
#endif

            applyData();
            viewPoints.Clear();

#if UNITY_EDITOR
            Profiler.EndSample();
#endif
        }
        float getEuler()
        {
            return transform.eulerAngles.z;
        }
        Vector3 getEyePos()
        {
            return transform.position;
        }
        Vector3 hiderPosition;
        void revealHiders()
        {
            FogOfWarHider hiderInQuestion;
            float distToHider;
            float sightDist = viewRadius;
            if (revealHidersInFadeOutZone && FogOfWarWorld.instance.usingBlur)
                sightDist += FogOfWarWorld.instance.softenDistance;
            for (int i = 0; i < FogOfWarWorld.numHiders; i++)
            {
                hiderInQuestion = FogOfWarWorld.hiders[i];
                bool seen = false;
                Transform samplePoint;
                float minDistToHider = distBetweenVectors(hiderInQuestion.transform.position, getEyePos()) - hiderInQuestion.maxDistBetweenPoints;
                if (minDistToHider < unobscuredRadius || (minDistToHider < sightDist))
                {
                    for (int j = 0; j < hiderInQuestion.samplePoints.Length; j++)
                    {
                        samplePoint = hiderInQuestion.samplePoints[j];
                        distToHider = distBetweenVectors(samplePoint.position, getEyePos());
                        if (distToHider < unobscuredRadius || (distToHider < sightDist && Mathf.Abs(AngleBetweenVector2(samplePoint.position - getEyePos(), getForward())) < viewAngle / 2))
                        {
                            //hiderPosition.x = samplePoint.position.x;
                            //hiderPosition.y = getEyePos().y;
                            //hiderPosition.z = samplePoint.position.z;
                            setHiderPosition(samplePoint.position);
                            if (!Physics2D.Raycast(getEyePos(), hiderPosition - getEyePos(), distToHider, obstacleMask))
                            {
                                seen = true;
                                break;
                            }
                        }
                    }
                }
                if (seen)
                {
                    if (!hidersSeen.Contains(hiderInQuestion))
                    {
                        hidersSeen.Add(hiderInQuestion);
                        hiderInQuestion.addSeer(this);
                    }
                }
                else
                {
                    if (hidersSeen.Contains(hiderInQuestion))
                    {
                        hidersSeen.Remove(hiderInQuestion);
                        hiderInQuestion.removeSeer(this);
                    }
                }
            }
        }
        void setHiderPosition(Vector3 point)
        {
            hiderPosition.x = point.x;
            hiderPosition.y = point.y;
            //hiderPosition.z = getEyePos().z;
        }
        protected override bool testPoint(Vector3 point)
        {
            float sightDist = viewRadius;
            if (revealHidersInFadeOutZone && FogOfWarWorld.instance.usingBlur)
                sightDist += FogOfWarWorld.instance.softenDistance;

            float distToPoint = distBetweenVectors(point, getEyePos());
            if (distToPoint < unobscuredRadius || (distToPoint < sightDist && Mathf.Abs(AngleBetweenVector2(point - getEyePos(), getForward())) < viewAngle / 2))
            {
                setHiderPosition(point);
                if (!Physics2D.Raycast(getEyePos(), hiderPosition - transform.position, distToPoint, obstacleMask))
                    return true;
            }
            return false;
        }

        FogOfWarWorld.CircleStruct circleStruct;
        Vector2 center = new Vector2();
        public float[] radii;
        public float[] distances;
        public bool[] areHits;
        public void applyData()
        {
            radii = new float[viewPoints.Count];
            distances = new float[radii.Length];
            areHits = new bool[radii.Length];

#if UNITY_EDITOR
            if (debugMode)
            {
                Random.InitState(1);
            }
#endif

            for (int i = 0; i < radii.Length; i++)
            {
                //Vector3 difference = viewPoints[i].point - transform.position;
                //float deg = Mathf.Atan2(difference.z, difference.x) * Mathf.Rad2Deg;
                //deg = (deg + 360) % 360;
#if UNITY_EDITOR
                if (debugMode)
                {
                    //Debug.Log(deg);
                    Debug.DrawRay(getEyePos(), (viewPoints[i].point - getEyePos()) + Random.insideUnitSphere * drawRayNoise, Color.blue);

                    if (i != 0)
                        Debug.DrawLine(viewPoints[i].point, viewPoints[i - 1].point, Color.yellow);
                }
#endif
                radii[i] = viewPoints[i].angle;
                areHits[i] = viewPoints[i].hit;
                distances[i] = viewPoints[i].dst;
                if (i == radii.Length - 1 && circleIsComplete)
                {
                    radii[i] += 360;
                }
            }

            center.x = getEyePos().x;
            center.y = getEyePos().y;
            //if (FogOfWarWorld.instance.gamePlane == FogOfWarWorld.GamePlane.XZ)
            //{
            //    center.x = transform.position.x;
            //    center.y = transform.position.z;
            //}
            //else if (FogOfWarWorld.instance.gamePlane == FogOfWarWorld.GamePlane.XY)
            //{
            //    center.x = transform.position.x;
            //    center.y = transform.position.y;
            //}
            //else
            //{
            //    center.x = transform.position.z;
            //    center.y = transform.position.y;
            //}

            circleStruct.circleOrigin = center;
            circleStruct.numSegments = radii.Length;
            circleStruct.circleRadius = viewRadius;
            circleStruct.unobscuredRadius = unobscuredRadius;
            circleStruct.circleHeight = transform.position.z;
            circleStruct.visionHeight = visionHeight;
            circleStruct.isComplete = circleIsComplete ? 1 : 0;

            FogOfWarWorld.instance.updateCircle(fogOfWarID, circleStruct, radii, distances, areHits);
        }

        bool greaterThanLastAngle;
        void determineEdge(ViewCastInfo oldViewCast, ViewCastInfo newViewCast, int iteration = 0)
        {
            if (oldViewCast.hit != newViewCast.hit)
            {
                if (iteration >= numExtraIterations)
                {
                    EdgeInfo farEdge = FindEdge(newViewCast, oldViewCast, true);
                    EdgeInfo closeEdge = FindEdge(oldViewCast, newViewCast);
                    greaterThanLastAngle = farEdge.maxViewCast.angle > closeEdge.maxViewCast.angle;
                    bool noneAdded = true;
                    if (newViewCast.dst < oldViewCast.dst)
                    {
                        if (Mathf.Abs(closeEdge.minViewCast.dst - viewRadius) < .01f || Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                        {
                            viewPoints.Add(closeEdge.minViewCast);
                            viewPoints.Add(closeEdge.maxViewCast);
                            noneAdded = false;
                        }
                        else
                            greaterThanLastAngle = true;
                        if (Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f && greaterThanLastAngle)
                        {
                            viewPoints.Add(farEdge.maxViewCast);
                            viewPoints.Add(farEdge.minViewCast);
                            noneAdded = false;
                        }
                        //if (Mathf.Abs(closeEdge.minViewCast.dst - viewRadius) < .01f || Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                        //{
                        //	viewPoints.Add(closeEdge.minViewCast);
                        //	viewPoints.Add(closeEdge.maxViewCast);
                        //}
                        //if (Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f)
                        //{
                        //	viewPoints.Add(farEdge.maxViewCast);
                        //	viewPoints.Add(farEdge.minViewCast);
                        //}
                    }
                    else
                    {
                        if (Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                        {
                            viewPoints.Add(closeEdge.minViewCast);
                            viewPoints.Add(closeEdge.maxViewCast);
                            noneAdded = false;
                        }
                        else
                            greaterThanLastAngle = true;
                        if ((Mathf.Abs(farEdge.maxViewCast.dst - viewRadius) < .01f || Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f) && greaterThanLastAngle)
                        {
                            viewPoints.Add(farEdge.maxViewCast);
                            viewPoints.Add(farEdge.minViewCast);
                            noneAdded = false;
                        }
                        //if (Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                        //{
                        //	viewPoints.Add(closeEdge.minViewCast);
                        //	viewPoints.Add(closeEdge.maxViewCast);
                        //}
                        //if (Mathf.Abs(farEdge.maxViewCast.dst - viewRadius) < .01f || Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f)
                        //{
                        //	viewPoints.Add(farEdge.maxViewCast);
                        //	viewPoints.Add(farEdge.minViewCast);
                        //}
                    }
                }
                else
                {
                    castExtraRays(oldViewCast.angle, newViewCast.angle, oldViewCast, iteration + 1);
                }
            }
            else if (newViewCast.hit && oldViewCast.hit)
            {
                float ExpectedDelta = Vector3.Distance(expectedNextPoint, newViewCast.point);
                if (ExpectedDelta > doubleHitMaxDelta || Mathf.Abs(AngleBetweenVector2(newViewCast.normal, oldViewCast.normal)) > doubleHitMaxAngleDelta)
                {
                    if (iteration >= numExtraIterations)
                    {
                        bool noneAdded = true;
                        if (Vector3.Distance(newViewCast.point, oldViewCast.point) > doubleHitMaxDelta)
                        {
                            EdgeInfo farEdge = FindEdge(newViewCast, oldViewCast, true);
                            EdgeInfo closeEdge = FindEdge(oldViewCast, newViewCast);
                            greaterThanLastAngle = farEdge.maxViewCast.angle > closeEdge.maxViewCast.angle;
                            if (newViewCast.dst < oldViewCast.dst)
                            {
                                if (Mathf.Abs(closeEdge.minViewCast.dst - viewRadius) < .01f || Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                                {
                                    viewPoints.Add(closeEdge.minViewCast);
                                    viewPoints.Add(closeEdge.maxViewCast);
                                    noneAdded = false;
                                }
                                else
                                    greaterThanLastAngle = true;
                                if (Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f && greaterThanLastAngle)
                                {
                                    viewPoints.Add(farEdge.maxViewCast);
                                    viewPoints.Add(farEdge.minViewCast);
                                    noneAdded = false;
                                }
                            }
                            else
                            {
                                if (Mathf.Abs(closeEdge.minViewCast.dst - closeEdge.maxViewCast.dst) > .01f)
                                {
                                    viewPoints.Add(closeEdge.minViewCast);
                                    viewPoints.Add(closeEdge.maxViewCast);
                                    noneAdded = false;
                                }
                                else
                                    greaterThanLastAngle = true;
                                if ((Mathf.Abs(farEdge.maxViewCast.dst - viewRadius) < .01f || Mathf.Abs(farEdge.minViewCast.dst - farEdge.maxViewCast.dst) > .01f) && greaterThanLastAngle)
                                {
                                    viewPoints.Add(farEdge.maxViewCast);
                                    viewPoints.Add(farEdge.minViewCast);
                                    noneAdded = false;
                                }
                            }
                        }
                        if (noneAdded)
                        {
                            float deltaAngle = AngleBetweenVector2(newViewCast.normal, oldViewCast.normal);
                            if (deltaAngle < 0)
                            {
                                EdgeInfo edge = FindMax(newViewCast, oldViewCast);
                                viewPoints.Add(edge.maxViewCast);
                            }
                            else if (addCorners && deltaAngle > 0)
                            {
                                EdgeInfo edge = FindMax(newViewCast, oldViewCast);
                                viewPoints.Add(edge.maxViewCast);
                            }
                        }

                    }
                    else
                    {
                        castExtraRays(oldViewCast.angle, newViewCast.angle, oldViewCast, iteration + 1);
                    }
                }

            }
        }

        void castExtraRays(float minAngle, float maxAngle, ViewCastInfo oldViewCast, int iteration)
        {
            float newAngleChange = (maxAngle - minAngle) / numExtraRaysOnIteration;

            float angleC = 180 - (AngleBetweenVector2(-Vector3.Cross(oldViewCast.normal, FogOfWarWorld.upVector), -oldViewCast.direction.normalized) + newAngleChange);
            float nextDist = (oldViewCast.dst * Mathf.Sin(Mathf.Deg2Rad * newAngleChange)) / Mathf.Sin(Mathf.Deg2Rad * angleC);
            expectedNextPoint = oldViewCast.point + (-Vector3.Cross(oldViewCast.normal, FogOfWarWorld.upVector) * nextDist);

            for (int i = 0; i < numExtraRaysOnIteration + 1; i++)
            {
                float angle = minAngle + (newAngleChange * i);
                ViewCastInfo newViewCast = ViewCast(angle);

                determineEdge(oldViewCast, newViewCast, iteration);

                angleC = 180 - (Mathf.Abs(AngleBetweenVector2(-Vector3.Cross(newViewCast.normal, FogOfWarWorld.upVector), -newViewCast.direction.normalized)) + newAngleChange);
                nextDist = (newViewCast.dst * Mathf.Sin(Mathf.Deg2Rad * newAngleChange)) / Mathf.Sin(Mathf.Deg2Rad * angleC);
                expectedNextPoint = newViewCast.point + (-Vector3.Cross(newViewCast.normal, FogOfWarWorld.upVector) * nextDist);

#if UNITY_EDITOR
                if (debugMode && drawExtraCastLines)
                {
                    Vector3 dir = DirFromAngle(angle, true);
                    if (newViewCast.hit)
                        Debug.DrawRay(getEyePos(), dir * (newViewCast.dst), Color.green);
                    else
                        Debug.DrawRay(getEyePos(), dir * (newViewCast.dst), Color.red);
                    Debug.DrawLine(newViewCast.point, expectedNextPoint + FogOfWarWorld.upVector * (.1f / iteration), Random.ColorHSV());
                }
#endif

                oldViewCast = newViewCast;
            }
        }


        Vector2 vec1;
        Vector2 vec2;
        Vector2 vec1Rotated90;
        private float AngleBetweenVector2(Vector3 _vec1, Vector3 _vec2)
        {
            vec1.x = _vec1.x;
            vec1.y = _vec1.y;
            vec2.x = _vec2.x;
            vec2.y = _vec2.y;

            //vec1 = vec1.normalized;
            //vec2 = vec2.normalized;
            vec1Rotated90.x = -vec1.y;
            vec1Rotated90.y = vec1.x;
            //Vector2 vec1Rotated90 = new Vector2(-vec1.y, vec1.x);
            float sign = (Vector2.Dot(vec1Rotated90, vec2) < 0) ? -1.0f : 1.0f;
            return Vector2.Angle(vec1, vec2) * sign;
        }
        float distBetweenVectors(Vector3 _vec1, Vector3 _vec2)
        {
            vec1.x = _vec1.x;
            vec1.y = _vec1.y;
            vec2.x = _vec2.x;
            vec2.y = _vec2.y;
            return Vector2.Distance(vec1, vec2);
        }

        Vector3 getForward()
        {
            return new Vector3(-transform.up.x, transform.up.y, 0).normalized;
        }

        EdgeInfo FindEdge(ViewCastInfo minViewCast, ViewCastInfo maxViewCast, bool isReflect = false)
        {
            float minAngle = minViewCast.angle;
            float maxAngle = maxViewCast.angle;

            for (int i = 0; i < maxEdgeResolveIterations; i++)
            {
                float angle = (minAngle + maxAngle) / 2;
#if UNITY_EDITOR
                if (debugMode && drawIteritiveLines)
                {
                    Vector3 dir = DirFromAngle(angle, true);
                    Debug.DrawRay(getEyePos(), dir * (viewRadius + 2), Color.white);
                }
#endif
                ViewCastInfo newViewCast = ViewCast(angle);

                bool edgeDstThresholdExceeded = Mathf.Abs(minViewCast.dst - newViewCast.dst) > edgeDstThreshold;
                edgeDstThresholdExceeded = edgeDstThresholdExceeded || Mathf.Abs(AngleBetweenVector2(newViewCast.normal, minViewCast.normal)) > 0;

                if (newViewCast.hit == minViewCast.hit && !edgeDstThresholdExceeded)
                {
                    minViewCast = newViewCast;
                    minAngle = angle;
                }
                else
                {
                    maxViewCast = newViewCast;
                    maxAngle = angle;
                }
                if (Mathf.Abs(maxAngle - minAngle) < maxAcceptableEdgeAngleDifference)
                {
                    break;
                }
            }

            return new EdgeInfo(minViewCast, maxViewCast, true);
            //return new EdgeInfo(minPoint, maxPoint);
        }
        EdgeInfo FindMax(ViewCastInfo minViewCast, ViewCastInfo maxViewCast)
        {
            float minAngle = minViewCast.angle;
            float maxAngle = maxViewCast.angle;

            for (int i = 0; i < maxEdgeResolveIterations; i++)
            {
                float angle = (minAngle + maxAngle) / 2;
#if UNITY_EDITOR
                if (debugMode && drawIteritiveLines)
                {
                    Vector3 dir = DirFromAngle(angle, true);
                    Debug.DrawRay(getEyePos(), dir * (viewRadius + 2), Color.white);
                }
#endif
                ViewCastInfo newViewCast = ViewCast(angle);

                bool edgeDstThresholdExceeded = Mathf.Abs(minViewCast.dst - newViewCast.dst) > edgeDstThreshold;
                edgeDstThresholdExceeded = edgeDstThresholdExceeded || Mathf.Abs(AngleBetweenVector2(newViewCast.normal, minViewCast.normal)) > 0;
                if (newViewCast.hit == minViewCast.hit && !edgeDstThresholdExceeded)
                {
                    minViewCast = newViewCast;
                    minAngle = angle;
                }
                else
                {
                    maxViewCast = newViewCast;
                    maxAngle = angle;
                }
                if (Mathf.Abs(maxAngle - minAngle) < maxAcceptableEdgeAngleDifference)
                {
                    break;
                }
            }

            return new EdgeInfo(minViewCast, maxViewCast, true);
        }

        RaycastHit2D rayHit;
        ViewCastInfo ViewCast(float globalAngle)
        {
#if UNITY_EDITOR
            numRayCasts++;
#endif
            Vector3 dir = DirFromAngle(globalAngle, true);

            float rayDist = viewRadius;
            if (FogOfWarWorld.instance.usingBlur)
                rayDist += FogOfWarWorld.instance.softenDistance;
            rayHit = Physics2D.Raycast(getEyePos(), dir, rayDist, obstacleMask);
            if (rayHit.collider != null)
            {
                return new ViewCastInfo(true, rayHit.point, rayHit.distance, globalAngle, rayHit.normal, dir);
            }
            else
            {
                return new ViewCastInfo(false, getEyePos() + dir * viewRadius, viewRadius, globalAngle, Vector3.zero, dir);
            }
        }

        Vector3 direction = Vector3.zero;
        public Vector3 DirFromAngle(float angleInDegrees, bool angleIsGlobal)
        {
            if (!angleIsGlobal)
            {
                angleInDegrees += transform.eulerAngles.z;
            }
            direction.x = Mathf.Cos(angleInDegrees * Mathf.Deg2Rad);
            direction.y = Mathf.Sin(angleInDegrees * Mathf.Deg2Rad);
            return direction;
        }
    }
}
