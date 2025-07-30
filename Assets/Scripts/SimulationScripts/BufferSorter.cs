using UnityEngine;
using static UnityEngine.Mathf;

public class BufferSorter
{
    const int sortKernel = 0;
    const int offsetKernel = 1;

    readonly ComputeShader sortCompute;
    int indexBufferCount;
    int sortStageCount;

    public BufferSorter()
    {
        sortCompute = ComputeHelper.LoadComputeShader("GridSorter");
    }

    // the index and offset buffers are passed into here from the ParticleSimulator3D script and set as the buffers for the sort compute shader
    // compute shaders can share buffer data and both will see changes made by the other shader to that buffer
    public void SetBuffers(ComputeBuffer indexBuffer, ComputeBuffer offsetBuffer)
    {
        indexBufferCount = indexBuffer.count;

        ComputeHelper.SetBuffer(sortCompute, offsetBuffer, "Offsets", offsetKernel);
        ComputeHelper.SetBuffer(sortCompute, indexBuffer, "Entries", offsetKernel, sortKernel);

        sortCompute.SetInt("numEntries", indexBufferCount);
        sortStageCount = (int)Log(NextPowerOfTwo(indexBufferCount), 2);
    }

    
    public void Sort()
    {
        for (int stageIndex = 0; stageIndex < sortStageCount; stageIndex++)
        {
            for (int stepIndex = 0; stepIndex < stageIndex + 1; stepIndex++)
            {
                int groupWidth = 1 << (stageIndex - stepIndex);
                int groupHeight = 2 * groupWidth - 1;
                sortCompute.SetInt("groupWidth", groupWidth);
                sortCompute.SetInt("groupHeight", groupHeight);
                sortCompute.SetInt("stepIndex", stepIndex);
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBufferCount) / 2, 0);
            }
        }
    }

    public void SortAndCalcOffsets()
    {
        Sort();

        ComputeHelper.Dispatch(sortCompute, indexBufferCount, offsetKernel);
    }
}
