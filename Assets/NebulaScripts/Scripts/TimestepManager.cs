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

    // kernel ID
    const int reductionKernel = 0;

    // parameters
    int simParticleCount;

    public TimestepManager()
    {
        reductionCompute = ComputeHelper.LoadComputeShader("TimestepReduction");
    }

    public void SetBuffers(ComputeBuffer deltaTimeBuffer, ComputeBuffer globalDeltaTimeBuffer, int particleCount)
    {
        simParticleCount = particleCount;
        L0 = deltaTimeBuffer;
        L3 = globalDeltaTimeBuffer;
        InitialiseBuffers();
    }

    private void InitialiseBuffers()
    {
        L1 = ComputeHelper.CreateStructuredBuffer<float>(256*256);
        L2 = ComputeHelper.CreateStructuredBuffer<float>(256);
    }

    public void PerformReduction()
    {

    }
}
