#ifndef _CONESETUP_
#define _CONESETUP_

struct circleStruct
{
    float2 circleOrigin;
    int startIndex;
    int numSegments;
    float circleRadius;
    float circleHeight;
    float visionHeight;
    float unobscuredRadius;
    bool isComplete;
};
struct ConeEdgeStruct
{
    float edgeAngle;
    float length;
    bool cutShort;
};

float _extraRadius;

float _fadeOutDegrees;
float _fadeOutDistance;
float _unboscuredFadeOutDistance;

int _NumCircles;
StructuredBuffer<int> _ActiveCircleIndices;
StructuredBuffer<circleStruct> _CircleBuffer;
StructuredBuffer<ConeEdgeStruct> _ConeBuffer;

//int _fadeType;
float _fadePower;
void CustomCurve_float(float In, out float Out)
{
    Out = In; //fade type 1; linear

#if FADE_SMOOTH
    Out = sin(Out * 1.570796);  //smooth fade
    //Out = pow(Out, 10); //exponential fade
#elif FADE_EXP
    Out = pow(Out, _fadePower); //exponential fade
#endif

    //Out = In; //fade type 1; linear
    //if (_fadeType == 0)  //smooth fade
    //    Out = sin(Out * 1.570796);
    //else if (_fadeType == 2) //exponential fade
    //    Out = pow(Out, _fadePower);
}

void NoBleedCheck_float(float2 Position, float height, out float Out)
{
    Out = 0;
    for (int i = 0; i < _NumCircles; i++)
    {
        circleStruct circle = _CircleBuffer[_ActiveCircleIndices[i]];
        float distToCircleOrigin = distance(Position, circle.circleOrigin);
        if (distToCircleOrigin < circle.circleRadius)
        {
            float heightDist = abs(height - circle.circleHeight);
            if (heightDist > circle.visionHeight)
                continue;
            float2 relativePosition = Position - circle.circleOrigin;
            float deg = degrees(atan2(relativePosition.y, relativePosition.x));
            
            ConeEdgeStruct previousCone = _ConeBuffer[circle.startIndex];
            for (int c = 0; c < circle.numSegments; c++)
            {
                float prevAng = previousCone.edgeAngle;
                ConeEdgeStruct currentCone = _ConeBuffer[circle.startIndex + c];
                
                if (circle.isComplete)
                {
                    deg = (deg + 360) % 360;
                    if (c == circle.numSegments - 1 && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                else
                {
                    if (prevAng > 0 && currentCone.edgeAngle > 0)
                    {
                        deg = (deg + 360) % 360;
                    }
                    if ((prevAng > 360 || currentCone.edgeAngle > 360) && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                
                if (deg > prevAng && currentCone.edgeAngle > deg)
                {
                    //old method
                    //float angle_a = currentCone.edgeAngle - previousCone.edgeAngle;
                    //float long_side = sqrt((previousCone.length * previousCone.length) + (currentCone.length * currentCone.length) - (2 * previousCone.length * currentCone.length * cos(radians(angle_a)))); //law of cosines
                    
                    //float angle_b = asin(currentCone.length * sin(radians(angle_a)) / long_side); //law of sines
                    //float angle_a1 = 180 - (deg - previousCone.edgeAngle + degrees(angle_b));
                    //float distToEdge = previousCone.length * sin(angle_b) / sin(radians(angle_a1));
                    
                    //float2 lowerPoint = float2(sin(radians(previousCone.edgeAngle)), cos(radians(previousCone.edgeAngle))) * previousCone.length;
                    //float2 upperPoint = float2(sin(radians(deg)), cos(radians(deg))) * distToEdge;
                    
                    //float lerpVal = distance(lowerPoint, upperPoint) / long_side;
                    

                    float lerpVal = (deg - prevAng) / (currentCone.edgeAngle - prevAng);
                    float distToConeEnd = lerp(previousCone.length, currentCone.length, lerpVal);
                    
                    //if (abs(previousCone.length - circle.circleRadius) > .001 || abs(currentCone.length - circle.circleRadius) > .001)
                    if (previousCone.cutShort && currentCone.cutShort)
                    {
                        float2 start = circle.circleOrigin + float2(cos(radians(prevAng)), sin(radians(prevAng))) * previousCone.length;
                        float2 end = circle.circleOrigin + float2(cos(radians(currentCone.edgeAngle)), sin(radians(currentCone.edgeAngle))) * currentCone.length;
                        
                        float a1 = end.y - start.y;
                        float b1 = start.x - end.x;
                        float c1 = a1 * start.x + b1 * start.y;
                    
                        float a2 = Position.y - circle.circleOrigin.y;
                        float b2 = circle.circleOrigin.x - Position.x;
                        float c2 = a2 * circle.circleOrigin.x + b2 * circle.circleOrigin.y;
                    
                        float determinant = (a1 * b2) - (a2 * b1);
                    
                        float x = (b2 * c1 - b1 * c2) / determinant;
                        float y = (a1 * c2 - a2 * c1) / determinant;
                    
                        float2 intercection = float2(x, y);
                        distToConeEnd = distance(intercection, circle.circleOrigin) + _extraRadius;
                    }
                    distToConeEnd = max(distToConeEnd, circle.unobscuredRadius);
                    
                    if (distToCircleOrigin < distToConeEnd)
                    {
                        Out = 1;
                        return;
                    }
                }
                
                previousCone = currentCone;
            }
            if (distToCircleOrigin < circle.unobscuredRadius)
            {
                Out = 1;
                return;
            }
        }
    }
}

void NoBleedSoft_float(float2 Position, float height, out float Out)
{
    Out = 0;
    for (int i = 0; i < _NumCircles; i++)
    {
        circleStruct circle = _CircleBuffer[_ActiveCircleIndices[i]];
        float distToCircleOrigin = distance(Position, circle.circleOrigin);
        if (distToCircleOrigin < circle.circleRadius + _fadeOutDistance)
        {
            float heightDist = abs(height - circle.circleHeight);
            if (heightDist > circle.visionHeight)
                continue;
            float2 relativePosition = Position - circle.circleOrigin;
            float deg = degrees(atan2(relativePosition.y, relativePosition.x));
            
            ConeEdgeStruct previousCone = _ConeBuffer[circle.startIndex];
            for (int c = 0; c < circle.numSegments; c++)
            {
                float prevAng = previousCone.edgeAngle;
                ConeEdgeStruct currentCone = _ConeBuffer[circle.startIndex + c];
                
                if (circle.isComplete)
                {
                    deg = (deg + 360) % 360;
                    if (c == circle.numSegments - 1 && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                else
                {
                    if (prevAng > 0 && currentCone.edgeAngle > 0)
                    {
                        deg = (deg + 360) % 360;
                    }
                    if ((prevAng > 360 || currentCone.edgeAngle > 360) && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                
                #if OUTER_SOFTEN
                if (deg > prevAng && currentCone.edgeAngle > deg)
                #elif INNER_SOFTEN
                if (deg > prevAng - _fadeOutDegrees && currentCone.edgeAngle + _fadeOutDegrees > deg)
                #endif
                {
                    float lerpVal = (deg - prevAng) / (currentCone.edgeAngle - prevAng);
                    float distToConeEnd = lerp(previousCone.length, currentCone.length, lerpVal);
                    
                    float newBlurDistance = (distToConeEnd / circle.circleRadius) * _fadeOutDistance;
                    
                    #if INNER_SOFTEN
                    if (deg < prevAng || currentCone.edgeAngle < deg)
                    {
                        float refe = currentCone.length;
                        if (deg < prevAng)
                            refe = previousCone.length;
                        float arcLen = (2 * (distToConeEnd * distToConeEnd)) - (2 * distToConeEnd * distToConeEnd * cos(radians(_fadeOutDegrees)));
                        float angDiff = (prevAng - deg) / _fadeOutDegrees;
                        angDiff = max(angDiff, (deg - currentCone.edgeAngle) / _fadeOutDegrees);
                        angDiff = max(angDiff, 0);
                        if (previousCone.cutShort && currentCone.cutShort)
                        {
                            newBlurDistance = 0;
                            if (distToConeEnd > circle.circleRadius)
                            {
                                newBlurDistance = distToConeEnd - circle.circleRadius;
                                distToConeEnd = circle.circleRadius;
                            }
                        }
                        else
                        {
                            distToConeEnd = min(previousCone.length, currentCone.length);
                            //distToConeEnd = refe;
                        }
                        //newBlurDistance+= arcLen;
                        if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                        {
                            if (distToCircleOrigin < distToConeEnd)
                            {
                                //Out = max(Out, (1 - angDiff));
                                //Out = max(Out, (1 - sin(angDiff * 1.570796)));
                                Out = max(Out, cos(angDiff * 1.570796));
                            }
                            //Out = max(Out, lerp(0, 1 - angDiff, ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                            //Out = max(Out, lerp(0, 1 - angDiff, clamp(((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance, 0,1)));
                            Out = max(Out, lerp(0, cos(angDiff * 1.570796), clamp(((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance, 0,1)));
                            //Out = max(Out, (1 - angDiff) * ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance);
                            //Out = max(Out, (1 - angDiff) * (1 - (distToCircleOrigin - distToConeEnd) / (_fadeOutDistance + circle.circleRadius - distToConeEnd)));
                            //Out = max(Out, clamp((1 - sin(angDiff * 1.570796)) * (1 - (distToCircleOrigin - distToConeEnd) / (_fadeOutDistance + circle.circleRadius - distToConeEnd)),0,1));
                            //Out = max(Out, 1 - sin(angDiff * 1.570796));
                        }
                        previousCone = currentCone;
                        continue;
                    }
                    #endif
                    //if (abs(previousCone.length - circle.circleRadius) > .001 || abs(currentCone.length - circle.circleRadius) > .001)
                    if (previousCone.cutShort && currentCone.cutShort)
                    {
                        float2 start = circle.circleOrigin + float2(cos(radians(prevAng)), sin(radians(prevAng))) * previousCone.length;
                        float2 end = circle.circleOrigin + float2(cos(radians(currentCone.edgeAngle)), sin(radians(currentCone.edgeAngle))) * currentCone.length;
                        
                        float a1 = end.y - start.y;
                        float b1 = start.x - end.x;
                        float c1 = a1 * start.x + b1 * start.y;
                    
                        float a2 = Position.y - circle.circleOrigin.y;
                        float b2 = circle.circleOrigin.x - Position.x;
                        float c2 = a2 * circle.circleOrigin.x + b2 * circle.circleOrigin.y;
                    
                        float determinant = (a1 * b2) - (a2 * b1);
                    
                        float x = (b2 * c1 - b1 * c2) / determinant;
                        float y = (a1 * c2 - a2 * c1) / determinant;
                    
                        float2 intercection = float2(x, y);
                        distToConeEnd = distance(intercection, circle.circleOrigin);
                        newBlurDistance = 0;
                        if (distToConeEnd > circle.circleRadius)
                        {
                            newBlurDistance = distToConeEnd - circle.circleRadius;
                            distToConeEnd = circle.circleRadius;
                        }
                        distToConeEnd += _extraRadius;
                        newBlurDistance += _extraRadius;
                    }
                    distToConeEnd = max(distToConeEnd, circle.unobscuredRadius);
                    
                    if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                    {
                        if (distToCircleOrigin < distToConeEnd)
                        {
                            Out = 1;
                            return;
                        }
                        Out = max(Out, lerp(0, 1, ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                        break;
                    }
                }
                
                previousCone = currentCone;
            }
            //if (distToCircleOrigin < circle.unobscuredRadius)
            //{
            //    Out = 1;
            //    return;
            //}
            if (distToCircleOrigin < circle.unobscuredRadius + _unboscuredFadeOutDistance)
            {
                if (distToCircleOrigin < circle.unobscuredRadius)
                {
                    Out = 1;
                    return;
                }
                Out = max(Out, lerp(1, 0, (distToCircleOrigin - circle.unobscuredRadius)/ _unboscuredFadeOutDistance));
            }
        }
    }
}

void FowHard_float(float2 Position, float height, out float Out)
{
    Out = 0;
    for (int i = 0; i < _NumCircles; i++)
    {
        circleStruct circle = _CircleBuffer[_ActiveCircleIndices[i]];
        float distToCircleOrigin = distance(Position, circle.circleOrigin);
        if (distToCircleOrigin < circle.circleRadius)
        {
            float heightDist = abs(height - circle.circleHeight);
            if (heightDist > circle.visionHeight)
                continue;
            float2 relativePosition = Position - circle.circleOrigin;
            float deg = degrees(atan2(relativePosition.y, relativePosition.x));
            //deg = (deg + 360) % 360;
            
            ConeEdgeStruct previousCone = _ConeBuffer[circle.startIndex];
            for (int c = 0; c < circle.numSegments; c++)
            {
                float prevAng = previousCone.edgeAngle;
                ConeEdgeStruct currentCone = _ConeBuffer[circle.startIndex + c];
                
                if (circle.isComplete)
                {
                    deg = (deg + 360) % 360;
                    if (c == circle.numSegments - 1 && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                else
                {
                    if (prevAng > 0 && currentCone.edgeAngle > 0)
                    {
                        deg = (deg + 360) % 360;
                    }
                    if ((prevAng > 360 || currentCone.edgeAngle > 360) && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                
                if (deg > prevAng && currentCone.edgeAngle > deg)
                {
                    float lerpVal = (deg - prevAng) / (currentCone.edgeAngle - prevAng);
                    float distToConeEnd = lerp(previousCone.length, currentCone.length, lerpVal);

                    if (previousCone.cutShort && currentCone.cutShort)
                    {
                        float2 start = circle.circleOrigin + float2(cos(radians(prevAng)), sin(radians(prevAng))) * previousCone.length;
                        float2 end = circle.circleOrigin + float2(cos(radians(currentCone.edgeAngle)), sin(radians(currentCone.edgeAngle))) * currentCone.length;
                        
                        float a1 = end.y - start.y;
                        float b1 = start.x - end.x;
                        float c1 = a1 * start.x + b1 * start.y;
                    
                        float a2 = Position.y - circle.circleOrigin.y;
                        float b2 = circle.circleOrigin.x - Position.x;
                        float c2 = a2 * circle.circleOrigin.x + b2 * circle.circleOrigin.y;
                    
                        float determinant = (a1 * b2) - (a2 * b1);
                    
                        float x = (b2 * c1 - b1 * c2) / determinant;
                        float y = (a1 * c2 - a2 * c1) / determinant;
                    
                        float2 intercection = float2(x, y);
                        distToConeEnd = distance(intercection, circle.circleOrigin) + _extraRadius;
                        
                        //to add the cone
                        float2 rotPoint = (start + end) / 2;
                        float2 arcOrigin = rotPoint + (float2(-(end.y - rotPoint.y), (end.x - rotPoint.x)) * 3);
                        float arcLength = distance(start, arcOrigin);
                        float2 newRelativePosition = arcOrigin + normalize(Position - arcOrigin) * arcLength;
                        distToConeEnd += distance(intercection, newRelativePosition) / 2;
                    }
                    distToConeEnd = max(distToConeEnd, circle.unobscuredRadius);
                    
                    if (distToCircleOrigin < distToConeEnd)
                    {
                        Out = 1;
                        return;
                    }
                }
                
                previousCone = currentCone;
            }
            if (distToCircleOrigin < circle.unobscuredRadius)
            {
                Out = 1;
                return;
            }
        }
    }
}

void FowSoft_float(float2 Position, float height, out float Out)
{
    Out = 0;
    for (int i = 0; i < _NumCircles; i++)
    {
        circleStruct circle = _CircleBuffer[_ActiveCircleIndices[i]];
        float distToCircleOrigin = distance(Position, circle.circleOrigin);
        if (distToCircleOrigin < circle.circleRadius + _fadeOutDistance)
        {
            float heightDist = abs(height - circle.circleHeight);
            if (heightDist > circle.visionHeight)
                continue;
            float2 relativePosition = Position - circle.circleOrigin;
            float deg = degrees(atan2(relativePosition.y, relativePosition.x));
            //deg = (deg + 360) % 360;
            
            ConeEdgeStruct previousCone = _ConeBuffer[circle.startIndex];
            
            for (int c = 0; c < circle.numSegments; c++)
            {
                float prevAng = previousCone.edgeAngle;
                ConeEdgeStruct currentCone = _ConeBuffer[circle.startIndex + c];
                
                if (circle.isComplete)
                {
                    deg = (deg + 360) % 360;
                    if (c == circle.numSegments - 1 && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                else
                {
                    if (prevAng > 0 && currentCone.edgeAngle > 0)
                    {
                        deg = (deg + 360) % 360;
                    }
                    if ((prevAng > 360 || currentCone.edgeAngle > 360) && deg < prevAng)
                    {
                        deg += 360;
                    }
                }
                
                #if OUTER_SOFTEN
                if (deg > prevAng && currentCone.edgeAngle > deg)
                #elif INNER_SOFTEN
                if (deg > prevAng - _fadeOutDegrees && currentCone.edgeAngle + _fadeOutDegrees > deg)
                #endif
                {
                    float lerpVal = clamp((deg - prevAng) / (currentCone.edgeAngle - prevAng),0,1);
                    float distToConeEnd = lerp(previousCone.length, currentCone.length, lerpVal);
                    
                    float newBlurDistance = (distToConeEnd / circle.circleRadius) * _fadeOutDistance;
                    
                    #if INNER_SOFTEN
                    if (deg < prevAng || currentCone.edgeAngle < deg)
                    {
                        float refe = currentCone.length;
                        if (deg < prevAng)
                            refe = previousCone.length;
                        float arcLen = (2 * (distToConeEnd * distToConeEnd)) - (2 * distToConeEnd * distToConeEnd * cos(radians(_fadeOutDegrees)));
                        float angDiff = (prevAng - deg) / _fadeOutDegrees;
                        angDiff = max(angDiff, (deg - currentCone.edgeAngle) / _fadeOutDegrees);
                        angDiff = max(angDiff, 0);
                        if (previousCone.cutShort && currentCone.cutShort)
                        {
                            newBlurDistance = 0;
                            if (distToConeEnd > circle.circleRadius)
                            {
                                newBlurDistance = distToConeEnd - circle.circleRadius;
                                distToConeEnd = circle.circleRadius;
                            }
                        }
                        else
                        {
                            distToConeEnd = min(previousCone.length, currentCone.length);
                            //distToConeEnd = refe;
                        }
                        //newBlurDistance+= arcLen;
                        if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                        {
                            if (distToCircleOrigin < distToConeEnd)
                            {
                                //Out = max(Out, (1 - angDiff));
                                //Out = max(Out, (1 - sin(angDiff * 1.570796)));
                                Out = max(Out, cos(angDiff * 1.570796));
                            }
                            //Out = max(Out, lerp(0, 1 - angDiff, ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                            //Out = max(Out, lerp(0, 1 - angDiff, clamp(((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance, 0,1)));
                            Out = max(Out, lerp(0, cos(angDiff * 1.570796), clamp(((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance, 0,1)));
                            //Out = max(Out, (1 - angDiff) * ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance);
                            //Out = max(Out, (1 - angDiff) * (1 - (distToCircleOrigin - distToConeEnd) / (_fadeOutDistance + circle.circleRadius - distToConeEnd)));
                            //Out = max(Out, clamp((1 - sin(angDiff * 1.570796)) * (1 - (distToCircleOrigin - distToConeEnd) / (_fadeOutDistance + circle.circleRadius - distToConeEnd)),0,1));
                            //Out = max(Out, 1 - sin(angDiff * 1.570796));
                        }
                        previousCone = currentCone;
                        continue;
                    }
                    #endif

                    //if (abs(previousCone.length - circle.circleRadius) > .001 || abs(currentCone.length - circle.circleRadius) > .001)
                    if (previousCone.cutShort && currentCone.cutShort)
                    {
                        //previousCone = currentCone;
                        //continue;
                        float2 start = circle.circleOrigin + float2(cos(radians(prevAng)), sin(radians(prevAng))) * previousCone.length;
                        float2 end = circle.circleOrigin + float2(cos(radians(currentCone.edgeAngle)), sin(radians(currentCone.edgeAngle))) * currentCone.length;
                        
                        float a1 = end.y - start.y;
                        float b1 = start.x - end.x;
                        float c1 = a1 * start.x + b1 * start.y;
                    
                        float a2 = Position.y - circle.circleOrigin.y;
                        float b2 = circle.circleOrigin.x - Position.x;
                        float c2 = a2 * circle.circleOrigin.x + b2 * circle.circleOrigin.y;
                    
                        float determinant = (a1 * b2) - (a2 * b1);
                    
                        float x = (b2 * c1 - b1 * c2) / determinant;
                        float y = (a1 * c2 - a2 * c1) / determinant;
                    
                        float2 intercection = float2(x, y);
                        distToConeEnd = distance(intercection, circle.circleOrigin) + _extraRadius;
                        
                        newBlurDistance = 0;
                        if (distToConeEnd > circle.circleRadius)
                        {
                            newBlurDistance = distToConeEnd - circle.circleRadius;
                            distToConeEnd = circle.circleRadius;
                        }
                        distToConeEnd += _extraRadius;
                        newBlurDistance += _extraRadius;
                        
                        //to add the cone
                        float2 rotPoint = (start + end) / 2;
                        float2 arcOrigin = rotPoint + (float2(-(end.y - rotPoint.y), (end.x - rotPoint.x)) * 3);
                        float arcLength = distance(start, arcOrigin);
                        float2 newRelativePosition = arcOrigin + normalize(Position - arcOrigin) * arcLength;
                        newBlurDistance += distance(intercection, newRelativePosition) / 2;
                    }
                    distToConeEnd = max(distToConeEnd, circle.unobscuredRadius);
                    
                    if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                    {
                        if (distToCircleOrigin < distToConeEnd)
                        {
                            Out = 1;
                            return;
                        }
                            Out = max(Out, lerp(0, 1, ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                            break;
                    }
                    //if (deg > prevAng && currentCone.edgeAngle > deg)
                    //{
                        
                    //}
                    //else
                    //{
                    //    if (previousCone.cutShort && currentCone.cutShort)
                    //    {
                    //        previousCone = currentCone;
                    //        continue;
                    //    }
                    //    distToConeEnd = min(previousCone.length, currentCone.length);
                    //    //distToConeEnd = previousCone.length;
                    //    //if (deg < prevAng)
                    //    //    distToConeEnd = currentCone.length;
                    //    //distToConeEnd-=_fadeOutDistance;
                    //    if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                    //    {
                    //        float angDiff = (prevAng - deg) / _fadeOutDegrees;
                    //        angDiff = max(angDiff, (deg - currentCone.edgeAngle) / _fadeOutDegrees);
                    //        angDiff = clamp(angDiff,0,1);
                    //        if (distToCircleOrigin < distToConeEnd)
                    //        {
                    //            Out =  max(Out, 1 - angDiff);
                    //        }
                    //        else
                    //        {
                    //            Out = max(Out, lerp(0, (1 - angDiff), ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                    //        }
                    //    }
                    //}
                    //if (deg < prevAng || currentCone.edgeAngle < deg)
                    //{
                    //    float angDiff = (prevAng - deg) / _fadeOutDegrees;
                    //    angDiff = max(angDiff, (deg - currentCone.edgeAngle) / _fadeOutDegrees);
                    //    if (distToCircleOrigin < distToConeEnd + newBlurDistance)
                    //    {
                    //        if (distToCircleOrigin < distToConeEnd)
                    //        {
                    //            //Out =  max(Out, 1 - angDiff);
                    //        }
                    //        else
                    //        {
                    //            Out = max(Out, lerp(0, 1 - angDiff, ((distToConeEnd + _fadeOutDistance) - distToCircleOrigin) / _fadeOutDistance));
                    //        }
                    //    }
                    //    continue;
                    //}
                }
                
                previousCone = currentCone;
            }
            if (distToCircleOrigin < circle.unobscuredRadius)
            {
                Out = 1;
                return;
            }
        }
    }
}
#endif