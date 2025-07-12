const float Pow2Factor;
const float Pow2DerivativeFactor;
const float Pow3Factor;
const float Pow3DerivativeFactor;
const float PolynomialPow6Factor;

float Pow2Influence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
    float x = smoothingRadius - dist;
    return x * x * Pow2Factor;
}

float Pow3Influence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
    float x = smoothingRadius - dist;
    return x * x * x * Pow3Factor;
}

float Pow2DerivativeInfluence(float dist, float smoothingRadius)
{
    if (dist > smoothingRadius) return 0.0f;
    
    float x = smoothingRadius - dist;
    return -x * Pow2DerivativeFactor;
}

float Pow3DerivativeInfluence(float dist, float smoothingRadius)
{
    if (dist > smoothingRadius) return 0.0f;
    
    float x = smoothingRadius - dist;
    return -x * x * Pow3DerivativeFactor;
}

float PolynomialPow6Influence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
    float x = smoothingRadius * smoothingRadius - dist * dist;
    return x * x * x * PolynomialPow6Factor;
}