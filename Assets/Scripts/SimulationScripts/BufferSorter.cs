using UnityEngine;
using static UnityEngine.Mathf;

public class BufferSorter
{
    const int sortKernel = 0;
    const int offsetKernel = 1;

    readonly ComputeShader sortCompute;
    ComputeBuffer indexBuffer;

    public BufferSorter()
    {
        sortCompute = ComputeHelper.LoadComputeShader("GridSorter");
    }

    public void SetBuffers(ComputeBuffer indexBuffer, ComputeBuffer offsetBuffer)
    {
        this.indexBuffer = indexBuffer;

        sortCompute.SetBuffer(sortKernel, "Entries", indexBuffer);
        ComputeHelper.SetBuffer(sortCompute, offsetBuffer, "Offsets", offsetKernel);
        ComputeHelper.SetBuffer(sortCompute, indexBuffer, "Entries", offsetKernel);
    }

    // This function sorts the 'indexBuffer' buffer using the 'bitonic merge sort' method
    public void Sort()
    {
        sortCompute.SetInt("numEntries", indexBuffer.count);

        int numStages = (int)Log(NextPowerOfTwo(indexBuffer.count), 2);

        for (int stageIndex = 0; stageIndex < numStages; stageIndex++)
        {
            for (int stepIndex = 0; stepIndex < stageIndex; stepIndex++)
            {
                int groupWidth = 1 << (stageIndex - stepIndex);
                int groupHeight = 2 * groupWidth - 1;
                sortCompute.SetInt("groupWidth", groupWidth);
                sortCompute.SetInt("groupHeight", groupHeight);
                sortCompute.SetInt("stepIndex", stepIndex);
                ComputeHelper.Dispatch(sortCompute, NextPowerOfTwo(indexBuffer.count) / 2, 0);
            }
        }
    }

    public void SortAndCalcOffsets()
    {
        Sort();

        ComputeHelper.Dispatch(sortCompute, indexBuffer.count, offsetKernel);
    }
}
