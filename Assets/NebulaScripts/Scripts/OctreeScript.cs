using UnityEngine;
using Unity.Mathematics;
using static UnityEngine.Mathf;
using System.Collections.Generic;

// NOTE: can not simulate more than 256^3 (~16.7M) particles due to reduction buffer size limits in this class
public class OctreeScript
{
    readonly ComputeShader reductionCompute;
    readonly ComputeShader octreeCompute;

    // buffers for each level of reduction
    ComputeBuffer L0; // upto 256^3 (~16.7M) entries and 256^2 (~65500) outputs into the L1Input buffer
    ComputeBuffer maxL1; // upto 256^2 (~65500) entries and ~256 outputs into the L2Input buffer
    ComputeBuffer maxL2; // upto 256 entries and 1 output into the L2Output buffer
    ComputeBuffer maxL3;

    ComputeBuffer minL1;
    ComputeBuffer minL2;
    ComputeBuffer minL3;

    ComputeBuffer mortonBuffer;
    ComputeBuffer binaryTreeBuffer;
    ComputeBuffer depthBuffer;

    // kernel ID
    const int reductionKernel = 0;
    const int mortonKeyKernel = 0;
    const int sortKernel = 1;
    const int BuildBinaryTreeKernel = 2;
    const int ComputeMassKernel = 3;

    // parameters
    int simParticleCount;
    int nodeCount;
    int sortStageCount;

    public OctreeScript()
    {
        reductionCompute = ComputeHelper.LoadComputeShader("WorldBoundsReduction");
        octreeCompute = ComputeHelper.LoadComputeShader("OctreeConstructor");
    }

    public void SetBuffers(ComputeBuffer octreeBuffer, ComputeBuffer positionBuffer, ComputeBuffer mortonKeyBuffer, int particleCount, float mass)
    {
        simParticleCount = particleCount;
        nodeCount = (simParticleCount * 2) - 1;
        L0 = positionBuffer;
        sortStageCount = (int)Log(NextPowerOfTwo(simParticleCount), 2);
        mortonBuffer = mortonKeyBuffer;
        binaryTreeBuffer = octreeBuffer;

        octreeCompute.SetInt("particleCount", simParticleCount);
        octreeCompute.SetInt("nodeCount", nodeCount);
        octreeCompute.SetFloat("particleMass", mass);
        ComputeHelper.SetBuffer(octreeCompute, octreeBuffer, "OctreeBuffer", mortonKeyKernel, BuildBinaryTreeKernel, ComputeMassKernel);
        ComputeHelper.SetBuffer(octreeCompute, positionBuffer, "PositionBuffer", mortonKeyKernel, BuildBinaryTreeKernel);
        ComputeHelper.SetBuffer(octreeCompute, mortonBuffer, "MortonBuffer", mortonKeyKernel, sortKernel, BuildBinaryTreeKernel);

        InitialiseBuffers();
    }

    private void InitialiseBuffers()
    {
        maxL1 = ComputeHelper.CreateStructuredBuffer<float4>(256 * 256);
        maxL2 = ComputeHelper.CreateStructuredBuffer<float4>(256);
        maxL3 = ComputeHelper.CreateStructuredBuffer<float4>(1);

        minL1 = ComputeHelper.CreateStructuredBuffer<float4>(256 * 256);
        minL2 = ComputeHelper.CreateStructuredBuffer<float4>(256);
        minL3 = ComputeHelper.CreateStructuredBuffer<float4>(1);

        depthBuffer = ComputeHelper.CreateStructuredBuffer<uint>(nodeCount);

        ComputeHelper.SetBuffer(octreeCompute, maxL3, "MaxBoundBuffer", mortonKeyKernel);
        ComputeHelper.SetBuffer(octreeCompute, minL3, "MinBoundBuffer", mortonKeyKernel);
        ComputeHelper.SetBuffer(octreeCompute, depthBuffer, "DepthBuffer", BuildBinaryTreeKernel, ComputeMassKernel);
    }

    public void ConstructOctree()
    {
        PerformReduction();
        sortMortonKeys();

        ComputeHelper.Dispatch(octreeCompute, simParticleCount, BuildBinaryTreeKernel);
        ComputeMasses();
    }

    private void PerformReduction()
    {
        // set buffers and dispatch reduction for L0 -> L1. (L0 size <= 256^3, L1 size <= 256^2)
        ComputeHelper.SetBuffer(reductionCompute, L0, "MaxInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, L0, "MinInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, maxL1, "MaxOutput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, minL1, "MinOutput", reductionKernel);
        reductionCompute.SetInt("inputSize", simParticleCount);
        ComputeHelper.Dispatch(reductionCompute, simParticleCount, reductionKernel);

        // set buffers and dispatch reduction for L1 -> L2. (L1 size <= 256^2, L2 size <= 256)
        ComputeHelper.SetBuffer(reductionCompute, maxL1, "MaxInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, minL1, "MinInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, maxL2, "MaxOutput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, minL2, "MinOutput", reductionKernel);
        int L1Size = (int)Mathf.Ceil(simParticleCount / 256.0f);
        reductionCompute.SetInt("inputSize", L1Size);
        ComputeHelper.Dispatch(reductionCompute, L1Size, reductionKernel);

        // set buffers and dispatch reduction for L2 -> L3. (L2 size <= 256, L3 size = 1)
        ComputeHelper.SetBuffer(reductionCompute, maxL2, "MaxInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, minL2, "MinInput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, maxL3, "MaxOutput", reductionKernel);
        ComputeHelper.SetBuffer(reductionCompute, minL3, "MinOutput", reductionKernel);
        int L2Size = (int)Mathf.Ceil(L1Size / 256.0f);
        reductionCompute.SetInt("inputSize", L2Size);
        ComputeHelper.Dispatch(reductionCompute, L2Size, reductionKernel);
    }

    private void sortMortonKeys()
    {
        ComputeHelper.Dispatch(octreeCompute, simParticleCount, mortonKeyKernel);

        for (int stageIndex = 0; stageIndex < sortStageCount; stageIndex++)
        {
            for (int stepIndex = 0; stepIndex < stageIndex + 1; stepIndex++)
            {
                int groupWidth = 1 << (stageIndex - stepIndex);
                int groupHeight = 2 * groupWidth - 1;
                octreeCompute.SetInt("groupWidth", groupWidth);
                octreeCompute.SetInt("groupHeight", groupHeight);
                octreeCompute.SetInt("stepIndex", stepIndex);
                ComputeHelper.Dispatch(octreeCompute, NextPowerOfTwo(simParticleCount) / 2, sortKernel);
            }
        }
    }

    // NOTE: this is terrible, make it better
    public void ComputeMasses()
    {
        for (int i = 1; i < 31; i++)
        {
            octreeCompute.SetInt("depthPass", i);
            ComputeHelper.Dispatch(octreeCompute, simParticleCount-1, ComputeMassKernel);
        }
    }

    private void DebugLog()
    {
        int totalNodes = (simParticleCount * 2) - 1;
        NewOctreeNode[] sortedKeys = new NewOctreeNode[totalNodes];
        binaryTreeBuffer.GetData(sortedKeys);

        Debug.Log($"root node mass: {sortedKeys[0].centerOfMass.w}");
    }

    public void ReleaseBuffers()
    {
        ComputeHelper.Release(L0, maxL1, maxL2, maxL3, minL1, minL2, minL3, depthBuffer);
    }
}

public struct NewOctreeNode
{
    public uint leftChild;
    public uint rightChild;
    public uint firstParticle;
    public uint particleCount;
    public float size;
    public float4 centerOfMass;
}
