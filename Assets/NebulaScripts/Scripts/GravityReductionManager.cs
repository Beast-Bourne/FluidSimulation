using UnityEngine;
using Unity.Mathematics;

// NOTE: can not simulate more than 256^3 (~16.7M) particles due to reduction buffer size limits in this class
public class GravityReductionManager
{
    readonly ComputeShader reductionCompute;

    // buffers for each level of reduction
    ComputeBuffer L0; // upto 256^3 (~16.7M) entries and 256^2 (~65500) outputs into the L1Input buffer
    ComputeBuffer L1; // upto 256^2 (~65500) entries and ~256 outputs into the L2Input buffer
    ComputeBuffer L2; // upto 256 entries and 1 output into the L2Output buffer
    ComputeBuffer L3;

    // kernel ID
    const int reductionKernel = 0;
    const int finalWriteKernel = 1;

    // parameters
    int simParticleCount;
    float convertFactor;

    public GravityReductionManager()
    {
        reductionCompute = ComputeHelper.LoadComputeShader("GravityReduction");
    }

    public void SetBuffers(ComputeBuffer gravityForceBuffer, ComputeBuffer gravityCorrectionBuffer, int particleCount)
    {
        simParticleCount = particleCount;
        L0 = gravityForceBuffer;

        convertFactor = -1.0f / (float)simParticleCount;
        reductionCompute.SetFloat("conversionFactor", convertFactor);

        InitialiseBuffers(gravityCorrectionBuffer);
    }

    private void InitialiseBuffers(ComputeBuffer correctionBuffer)
    {
        L1 = ComputeHelper.CreateStructuredBuffer<float3>(256 * 256);
        L2 = ComputeHelper.CreateStructuredBuffer<float3>(256);
        L3 = ComputeHelper.CreateStructuredBuffer<float3>(1);

        ComputeHelper.SetBuffer(reductionCompute, L3, "Input", finalWriteKernel);
        ComputeHelper.SetBuffer(reductionCompute, correctionBuffer, "Output", finalWriteKernel);
    }

    public void PerformReduction()
    {
        // set buffers and dispatch reduction for L0 -> L1. (L0 size <= 256^3, L1 size <= 256^2)
        ComputeHelper.SetBuffer(reductionCompute, L0, "Input", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, L1, "Output", reductionKernel);
        reductionCompute.SetInt("inputSize", simParticleCount);
        ComputeHelper.Dispatch(reductionCompute, simParticleCount, reductionKernel);

        // set buffers and dispatch reduction for L1 -> L2. (L1 size <= 256^2, L2 size <= 256)
        ComputeHelper.SetBuffer(reductionCompute, L1, "Input", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, L2, "Output", reductionKernel);
        int L1Size = (int)Mathf.Ceil(simParticleCount / 256.0f);
        reductionCompute.SetInt("inputSize", L1Size);
        ComputeHelper.Dispatch(reductionCompute, L1Size, reductionKernel);

        // set buffers and dispatch reduction for L2 -> L3. (L2 size <= 256, L3 size = 1)
        ComputeHelper.SetBuffer(reductionCompute, L2, "Input", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, L3, "Output", reductionKernel);
        int L2Size = (int)Mathf.Ceil(L1Size / 256.0f);
        reductionCompute.SetInt("inputSize", L2Size);
        ComputeHelper.Dispatch(reductionCompute, L2Size, reductionKernel);

        ComputeHelper.Dispatch(reductionCompute, 1, finalWriteKernel);
    }

    public void ReleaseBuffers()
    {
        ComputeHelper.Release(L0, L1, L2, L3);
    }
}
