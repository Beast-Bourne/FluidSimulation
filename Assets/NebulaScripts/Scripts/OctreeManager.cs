using UnityEngine;

public class OctreeManager : MonoBehaviour
{
    [SerializeField]
    private uint NumOfLayers;
    [SerializeField]
    private bool DisplayOctree;
    [SerializeField]
    private ComputeShader SpatialCompute;

    public OctreeNode[] Octree;
    [HideInInspector]
    public uint[] SpatialKeys;
    [HideInInspector]
    public uint[] SpatialHashes;
    [HideInInspector]
    public float BoundSize;

    private Vector3[] childOffsets = 
    {
        new Vector3(-1.0f, -1.0f, -1.0f),
        new Vector3(1.0f, -1.0f, -1.0f),
        new Vector3(-1.0f, 1.0f, -1.0f),
        new Vector3(1.0f, 1.0f, -1.0f),
        new Vector3(-1.0f, -1.0f, 1.0f),
        new Vector3(1.0f, -1.0f, 1.0f),
        new Vector3(-1.0f, 1.0f, 1.0f),
        new Vector3(1.0f, 1.0f, 1.0f)
    };

    public void BuildOctree(float minSize)
    {
        uint numOfNodes = (IntPow(8, NumOfLayers) - 1) / 7;
        Octree = new OctreeNode[numOfNodes];

        uint currentLayer = 1;
        uint CurrentLayerStartIndex = 0;
        uint NextLayerStartIndex = 1;
        uint EndOfDefinedNodesIndex = 0;
        Octree[0] = CreateRootNode(minSize);

        for (int i = 0; i < NumOfLayers - 1; i++)
        {
            GenerateNextLayer();
        }

        SetSpatialData();


        void GenerateNextLayer()
        {
            for (uint i = 0; i < numOfNodes; i++)
            {
                if (CurrentLayerStartIndex + i >= NextLayerStartIndex)
                {
                    CurrentLayerStartIndex = NextLayerStartIndex;
                    NextLayerStartIndex = EndOfDefinedNodesIndex + 1;
                    currentLayer++;
                    break;
                }

                float childSize = Octree[CurrentLayerStartIndex].size / 2.0f;
                GenerateChildren(CurrentLayerStartIndex + i, childSize);
            }
        }

        void GenerateChildren(uint ParentIndex, float childSize)
        {
            Octree[ParentIndex].FirstChildIndex = EndOfDefinedNodesIndex + 1;
            bool hasChildren = (currentLayer + 1 < NumOfLayers) ? true : false;

            for (uint i = 0; i < 8; i++)
            {
                EndOfDefinedNodesIndex++;
                Octree[EndOfDefinedNodesIndex].centre = Octree[ParentIndex].centre + (childOffsets[i] * (childSize / 2.0f));
                Octree[EndOfDefinedNodesIndex].size = childSize;
                Octree[EndOfDefinedNodesIndex].hasChildren = hasChildren;
                Octree[EndOfDefinedNodesIndex].ParentIndex = ParentIndex;
            }
        }
    }

    private void SetSpatialData()
    {

    }

    public void SetBuffers(ComputeBuffer OctreeBuffer, ComputeBuffer KeyBuffer, ComputeBuffer HashBuffer)
    {
        ComputeHelper.SetBuffer(SpatialCompute, OctreeBuffer, "Octree", 0);
        ComputeHelper.SetBuffer(SpatialCompute, KeyBuffer, "SpatialKeys", 0);
        ComputeHelper.SetBuffer(SpatialCompute, HashBuffer, "SpatialHashes", 0);
    }

    private OctreeNode CreateRootNode(float minSize)
    {
        OctreeNode rootNode = new OctreeNode();
        rootNode.centre = Vector3.zero;
        rootNode.size = minSize * IntPow(2, NumOfLayers - 1);
        rootNode.hasChildren = true;
        rootNode.FirstChildIndex = 1;
        rootNode.ParentIndex = 0;
        rootNode.SpatialStartIndex = 0;

        BoundSize = rootNode.size;

        return rootNode;
    }

    private uint IntPow(uint Base, uint Power)
    {
        uint result = 1;

        for (uint i = 0; i < Power; i++)
        {
            result *= Base;
        }

        return result;
    }

    private void OnDrawGizmos()
    {
        if (DisplayOctree && Octree != null)
        {
            Gizmos.color = Color.white;

            for (int i = 0; i < Octree.Length; i++)
            {
                OctreeNode node = Octree[i];
                Gizmos.DrawWireCube(node.centre, new Vector3(node.size, node.size, node.size));
            }
        }
    }
}

public struct OctreeNode
{
    public Vector3 centre;
    public float size;
    public bool hasChildren;
    public uint FirstChildIndex;
    public uint ParentIndex;
    public uint SpatialStartIndex;
}
