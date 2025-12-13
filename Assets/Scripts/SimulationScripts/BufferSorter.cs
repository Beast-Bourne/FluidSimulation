using UnityEngine;
using static UnityEngine.Mathf;

public class BufferSorter
{
    const int sortKernel = 0;
    const int offsetKernel = 1;
    const int sort2Kernel = 2;
    const int offset2Kernel = 3;
    const int sort3Kernel = 4;
    const int offset3Kernel = 5;

    readonly ComputeShader sortCompute;
    int indexBufferCount;
    int sortStageCount;
    int numIterations;

    // BufferSorter constructor initializes the compute shader used for sorting
    public BufferSorter()
    {
        sortCompute = ComputeHelper.LoadComputeShader("GridSorter");
    }

    // the index and offset buffers are passed into here from the ParticleSimulator3D script and set as the buffers for the sort compute shader
    // compute shaders can share buffer data and both will see changes made by the other shader to that buffer
    public void SetBuffers(ComputeBuffer indexBuffer, ComputeBuffer offsetBuffer)
    {
        indexBufferCount = indexBuffer.count;

        ComputeHelper.SetBuffer(sortCompute, offsetBuffer, "Offsets", offsetKernel, offset2Kernel, offset3Kernel);
        ComputeHelper.SetBuffer(sortCompute, indexBuffer, "Entries", offsetKernel, offset2Kernel, offset3Kernel, sortKernel, sort2Kernel, sort3Kernel);

        sortCompute.SetInt("numEntries", indexBufferCount);
        sortStageCount = (int)Log(NextPowerOfTwo(indexBufferCount), 2);
    }

    // sorts the index buffer using a bitonic sort method
    void Sort()
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
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBufferCount) / 2, sortKernel);
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBufferCount) / 2, sort2Kernel);
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBufferCount) / 2, sort3Kernel);
            }
        }
    }

    // runs the sort function then calculates the offsets for each index in the index buffer
    public void SortAndCalcOffsets()
    {
        Sort();

        ComputeHelper.Dispatch(sortCompute, indexBufferCount, offsetKernel);
        ComputeHelper.Dispatch(sortCompute, indexBufferCount, offset2Kernel);
        ComputeHelper.Dispatch(sortCompute, indexBufferCount, offset3Kernel);
    }
}
