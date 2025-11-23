const float Pow2Factor;
const float Pow2DerivativeFactor;
const float Pow3Factor;
const float Pow3DerivativeFactor;
const float PolynomialPow6Factor;
const float CubicSplineFactor;

// This script contains the definitions of influence functions for the different particle properties
// different influence functions are used because the behaviour of each property is different at distances close to 0

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

float CubicSplineInfluence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
    float q = dist / smoothingRadius;
    
    if (q <= 0.5f)
    {
        return (CubicSplineFactor * 6.0f * (q * q * q - q * q) + 1.0f);
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

float CubicSplineDerivativeInfluence(float dist, float smoothingRadius)
{
    if (dist >= smoothingRadius) return 0.0f;
    
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