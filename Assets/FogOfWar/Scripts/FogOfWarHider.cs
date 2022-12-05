using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FOW
{
    public class FogOfWarHider : MonoBehaviour
    {
        public GameObject[] objectsToHide;
        public Transform[] samplePoints;
        [HideInInspector] public float maxDistBetweenPoints;

        [HideInInspector] public int seenCount;
        [HideInInspector] public List<FogOfWarRevealer> seenBy = new List<FogOfWarRevealer>();

        private void OnEnable()
        {
            calculateSamplePointData();
            registerHider();
        }
        private void OnDisable()
        {
            setActive(true);
            deregisterHider();
        }

        void calculateSamplePointData()
        {
            if (samplePoints.Length == 0)
            {
                samplePoints = new Transform[1];
                samplePoints[0] = transform;
            }
            maxDistBetweenPoints = 0;
            for (int i = 0; i < samplePoints.Length; i++)
            {
                for (int j = i; j < samplePoints.Length; j++)
                {
                    maxDistBetweenPoints = Mathf.Max(maxDistBetweenPoints, Vector3.Distance(samplePoints[i].position, samplePoints[j].position));
                }
            }
        }

        public void registerHider()
        {
            //OnActiveChanged += callbackTest;
            if (!FogOfWarWorld.hiders.Contains(this))
            {
                FogOfWarWorld.hiders.Add(this);
                FogOfWarWorld.numHiders++;
                setActive(false);
            }
        }

        public void deregisterHider()
        {
            //OnActiveChanged -= callbackTest;
            if (FogOfWarWorld.hiders.Contains(this))
            {
                FogOfWarWorld.hiders.Remove(this);
                FogOfWarWorld.numHiders--;
                foreach (FogOfWarRevealer revealer in seenBy)
                {
                    revealer.hidersSeen.Remove(this);
                }
                seenCount = 0;
                seenBy.Clear();
            }
        }

        /// <summary>
        /// example method to show how to use the callback.
        /// </summary>
        void callbackTest(bool isActive)
        {
            Debug.Log(isActive);
        }

        public void addSeer(FogOfWarRevealer seer)   //see-er?
        {
            seenBy.Add(seer);
            if (seenCount == 0)
            {
                setActive(true);
            }
            seenCount++;
        }
        public void removeSeer(FogOfWarRevealer seer)   //see-er?
        {
            seenBy.Remove(seer);
            seenCount--;
            if (seenCount == 0)
            {
                setActive(false);
            }
        }


        public void modifyHiddenObjects(GameObject[] newObjectsToHide)
        {
            setActive(true);
            objectsToHide = newObjectsToHide;
            if (!enabled)
                return;

            if (seenCount == 0)
                setActive(false);
            else
                setActive(true);
        }

        public delegate void OnChangeActive(bool isActive);
        public event OnChangeActive OnActiveChanged;
        void setActive(bool isActive)
        {
            foreach (GameObject o in objectsToHide)
                o.SetActive(isActive);

            OnActiveChanged?.Invoke(isActive);
        }
    }
}