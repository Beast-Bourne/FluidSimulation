using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class ComputeHelper
{
    // runs the kernel from the compute shader with the specified index for the specified number of iterations
    public static void Dispatch(ComputeShader compute, int numIterationsX, int kernelIndex)
    {
        Vector3Int threadGroupSizes = GetThreadGroupSizes(compute, kernelIndex);
        int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
        int numGroupsY = Mathf.CeilToInt(1 / (float)threadGroupSizes.y);
        int numGroupsZ = Mathf.CeilToInt(1 / (float)threadGroupSizes.z);
        compute.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }

    // retrieves the x,y and z thread group sizes for the specified kernel index in the compute shader
    public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex)
    {
        uint x, y, z;
        compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
        return new Vector3Int((int)x, (int)y, (int)z);
    }

    // gets the size in bytes of type 'T'
    public static int GetStride<T>()
    {
        return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    }

    // creates a structured buffer of type 'T' with the specified count
    public static ComputeBuffer CreateStructuredBuffer<T>(int count)
    {
        return new ComputeBuffer(count, GetStride<T>());
    }

    // Sets a buffer in the compute shader for all specified kernels (allows the kernels to read and write to the buffer)
    public static void SetBuffer(ComputeShader compute, ComputeBuffer buffer, string id, params int[] kernels)
    {
        for (int i = 0; i < kernels.Length; i++)
        {
            compute.SetBuffer(kernels[i], id, buffer);
        }
    }

    // Releases the specified compute buffers (used for clean up on program close to prevent memory leaks)
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

    // Creates an argument buffer for drawing a mesh with instancing, using the specified mesh and number of instances
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

    // Loads a compute shader from the Resources folder using the specified shader name
    public static ComputeShader LoadComputeShader(string shaderName)
    {
        return Resources.Load<ComputeShader>(shaderName.Split('.')[0]);
    }
}
