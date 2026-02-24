using UnityEngine;

// NOTE: can not simulate more than 256^3 (~16.7M) particles due to reduction buffer size limits in this class
public class TimestepManager
{
    readonly ComputeShader reductionCompute;

    // buffers for each level of reduction
    ComputeBuffer L0; // upto 256^3 (~16.7M) entries and 256^2 (~65500) outputs into the L1Input buffer
    ComputeBuffer L1; // upto 256^2 (~65500) entries and ~256 outputs into the L2Input buffer
    ComputeBuffer L2; // upto 256 entries and 1 output into the L2Output buffer
    ComputeBuffer L3;
    ComputeBuffer Result;

    // kernel ID
    const int reductionKernel = 0;

    int L1Size;
    int L2Size;

    // parameters
    int simParticleCount;

    public TimestepManager()
    {
        reductionCompute = ComputeHelper.LoadComputeShader("TimestepReduction");
    }

    public void SetBuffers(ComputeBuffer deltaTimeBuffer, ComputeBuffer globalDeltaTimeBuffer, int particleCount)
    {
        simParticleCount = particleCount;
        L1Size = (int)Mathf.Ceil(simParticleCount / 256.0f);
        L2Size = (int)Mathf.Ceil(L1Size / 256.0f);

        L0 = deltaTimeBuffer;
        Result = globalDeltaTimeBuffer;
        InitialiseBuffers();
    }

    private void InitialiseBuffers()
    {
        L1 = ComputeHelper.CreateStructuredBuffer<float>(256*256);
        L2 = ComputeHelper.CreateStructuredBuffer<float>(256);
        L3 = ComputeHelper.CreateStructuredBuffer<float>(1);
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
        reductionCompute.SetInt("inputSize", L1Size);
        ComputeHelper.Dispatch(reductionCompute, L1Size, reductionKernel);

        // set buffers and dispatch reduction for L2 -> L3. (L2 size <= 256, L3 size = 1)
        ComputeHelper.SetBuffer(reductionCompute, L2, "Input", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, L3, "Output", reductionKernel);
        reductionCompute.SetInt("inputSize", L2Size);
        ComputeHelper.Dispatch(reductionCompute, L2Size, reductionKernel);

        float[] dt = new float[1];
        L3.GetData(dt);
        float newDt = dt[0];

        float[] oldResult = new float[1];
        Result.GetData(oldResult);
        float oldDt = oldResult[0];

        if (oldDt <= 0.0f)
        {
            Result.SetData(dt);
        }
        else
        {
            float finalDt = Mathf.Clamp(newDt, 0.75f*oldDt, 1.2f*oldDt);
            finalDt = Mathf.Min(finalDt, Time.deltaTime);
            float[] final = new float[1] { finalDt };
            Result.SetData(final);
        }
    }

    public void ReleaseBuffers()
    {
        ComputeHelper.Release(L0, L1, L2, L3, Result);
    }
}
