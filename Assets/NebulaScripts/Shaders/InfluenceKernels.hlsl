const float sigma;
const float C2Const;

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

float IntPower(float base, int exponent)
{
    float result = 1.0f;
    for (int i = 0; i < exponent; i++)
    {
        result *= base;
    }
    return result;
}

float WendlandC2(float dist, float smoothRad)
{
    float DimensionFactor = C2Const / (smoothRad * smoothRad * smoothRad);
    float q = dist / smoothRad;
    
    if (q > 2.0f || q < 0.0f) return 0.0f;

    float term1 = IntPower(1.0f - (q * 0.5f), 4);
    float term2 = (2.0f * q + 1.0f);
    
    return (DimensionFactor * term1 * term2);
}

float WendlandC2Derivative(float dist, float smoothRad)
{
    float DimensionFactor = C2Const / (smoothRad * smoothRad * smoothRad * smoothRad);
    float q = dist / smoothRad;
    if (q > 2.0f || q < 0.0f) return 0.0f;
    
    float term1 = IntPower(1.0f - (q * 0.5f), 3);
    return (-5.0f * q * DimensionFactor * term1);
}