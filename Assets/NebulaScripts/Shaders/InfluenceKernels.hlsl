const float sigma;

float Influence(float dist, float smoothingRadius)
{
    float DimensionFactor = sigma / (smoothingRadius*smoothingRadius*smoothingRadius);
    
    float q = dist / smoothingRadius;
    
    if (q <= 0.5f && q >= 0.0f)
    {
        return (DimensionFactor * (6.0f * (q * q * q - q * q) + 1.0f));
    }
    else if (q <= 1.0f && q > 0.5f)
    {
        float x = 1.0f - q;
        return (DimensionFactor * 2.0f * x * x * x);
    }
    else
    {
        return 0.0f;
    }
}

float DerivativeInfluence(float dist, float smoothingRadius)
{
    float DimensionFactor = sigma / (smoothingRadius * smoothingRadius * smoothingRadius * smoothingRadius);
    
    float q = dist / smoothingRadius;
    
    if (q <= 0.5f && q >= 0.0f)
    {
        return DimensionFactor * (18.0f * q * q - 12.0f * q);
    }
    else if (q <= 1.0f && q > 0.5f)
    {
        float x = 1.0f - q;
        return DimensionFactor * (-6.0f * x * x);
    }
    else
    {
        return 0.0f;
    }
}