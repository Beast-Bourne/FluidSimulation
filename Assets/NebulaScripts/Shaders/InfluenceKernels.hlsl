const float sigma;

float Influence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    float CubicSplineFactor = 3 / (4 * smoothingRadius);
    
    float q = dist / smoothingRadius;
    
    if (q <= 0.5f)
    {
        return (CubicSplineFactor * (6.0f * (q * q * q - q * q) + 1.0f));
    }
    else if (q <= 1.0f)
    {
        float x = 1.0f - q;
        return (CubicSplineFactor * 2.0f * x * x * x);
    }
    else
    {
        return 0.0f;
    }
}

float DerivativeInfluence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    float CubicSplineFactor = 3 / (4 * smoothingRadius);
    
    float q = dist / smoothingRadius;
    float invSR = 1.0f / smoothingRadius;
    
    if (q <= 0.5f)
    {
        return CubicSplineFactor * (18.0f * q * q * invSR - 12.0f * q * invSR);
    }
    else if (q <= 1.0f)
    {
        float x = 1.0f - q;
        return CubicSplineFactor * (-6.0f * x * x * invSR);
    }
    else
    {
        return 0.0f;
    }
}

// redundant with CubicSplineDerivativeInfluence but kept for memory of intent
float ViscocityLaplacianInfluence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
    float q = dist / smoothingRadius;
    
    return 6 * (1 - q) / (smoothingRadius * smoothingRadius);

}