using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class ComputeHelper
{
    public static void Dispatch(ComputeShader compute, int numIterationsX, int kernelIndex)
    {
        Vector3Int threadGroupSizes = GetThreadGroupSizes(compute, kernelIndex);
        int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
        int numGroupsY = Mathf.CeilToInt(1 / (float)threadGroupSizes.y);
        int numGroupsZ = Mathf.CeilToInt(1 / (float)threadGroupSizes.z);
        compute.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex)
    {
        uint x, y, z;
        compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
        return new Vector3Int((int)x, (int)y, (int)z);
    }

    public static int GetStride<T>()
    {
        return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    }

    public static ComputeBuffer CreateStructuredBuffer<T>(int count)
    {
        return new ComputeBuffer(count, GetStride<T>());
    }

    public static void SetBuffer(ComputeShader compute, ComputeBuffer buffer, string id, params int[] kernels)
    {
        for (int i = 0; i < kernels.Length; i++)
        {
            compute.SetBuffer(kernels[i], id, buffer);
        }
    }

    public static void Release(params ComputeBuffer[] buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            if (buffers[i] != null)
            {
                buffers[i].Release();
            }
        }
    }

    public static ComputeBuffer CreateArgsBuffer(Mesh mesh, int numInstances)
    {
        const int subMeshIndex = 0;
        uint[] args = new uint[5];
        args[0] = (uint)mesh.GetIndexCount(subMeshIndex);
        args[1] = (uint)numInstances;
        args[2] = (uint)mesh.GetIndexStart(subMeshIndex);
        args[3] = (uint)mesh.GetBaseVertex(subMeshIndex);
        args[4] = 0; // an offset?

        ComputeBuffer argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
        return argsBuffer;
    }

    public static ComputeShader LoadComputeShader(string shaderName)
    {
        return Resources.Load<ComputeShader>(shaderName.Split('.')[0]);
    }
}
