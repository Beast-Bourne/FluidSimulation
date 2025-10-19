using UnityEngine;

public class OctreeManager : MonoBehaviour
{
    [SerializeField]
    private uint NumOfLayers;
    [SerializeField]
    private bool DisplayOctree;
    [SerializeField]
    private ComputeShader SpatialCompute;

    private OctreeNode[] Octree;
    [HideInInspector]
    public uint[] SpatialKeys;
    [HideInInspector]
    public uint[] SpatialHashes;
    [HideInInspector]
    public float BoundSize;
    [HideInInspector]
    public int NumOfNodes { get { return ((int)IntPow(8, NumOfLayers) - 1) / 7; } }
    [HideInInspector]
    public int NumBottomLayerNodes { get { return (int)IntPow(8, NumOfLayers - 1); } }

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

    private void BuildOctree(float minSize)
    {
        Octree = new OctreeNode[NumOfNodes];

        uint currentLayer = 1;
        uint CurrentLayerStartIndex = 0;
        uint NextLayerStartIndex = 1;
        uint EndOfDefinedNodesIndex = 0;
        Octree[0] = CreateRootNode(minSize);

        for (int i = 0; i < NumOfLayers - 1; i++)
        {
            GenerateNextLayer();
        }

        void GenerateNextLayer()
        {
            for (uint i = 0; i < NumOfNodes; i++)
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
            uint hasChildren = (currentLayer + 1 < NumOfLayers) ? (uint)1 : 0;

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

    public void SetBuffers(ComputeBuffer OctreeBuffer, ComputeBuffer HashBuffer, float octreeMinSize, ComputeBuffer indexBuffer, ComputeBuffer offsetBuffer, int numParticles, ComputeBuffer posBuffer, float particleMass)
    {
        BuildOctree(octreeMinSize);

        OctreeNode[] allNodes = new OctreeNode[Octree.Length];
        System.Array.Copy(Octree, allNodes, Octree.Length);
        OctreeBuffer.SetData(allNodes);

        ComputeHelper.SetBuffer(SpatialCompute, OctreeBuffer, "Octree", 0, 1);
        ComputeHelper.SetBuffer(SpatialCompute, HashBuffer, "SpatialHashes", 0, 1);
        ComputeHelper.SetBuffer(SpatialCompute, indexBuffer, "SpatialIndices", 1);
        ComputeHelper.SetBuffer(SpatialCompute, offsetBuffer, "SpatialOffsets", 1);
        ComputeHelper.SetBuffer(SpatialCompute, posBuffer, "Positions", 1);

        SpatialCompute.SetInt("bottomLayerStartIndex", ((int)IntPow(8, NumOfLayers-1) - 1) / 7);
        SpatialCompute.SetInt("numBottomLayerNodes", NumBottomLayerNodes);
        SpatialCompute.SetFloat("smoothingRadius", octreeMinSize/2.0f);
        SpatialCompute.SetInt("numParticles", numParticles);
        SpatialCompute.SetFloat("particleMass", particleMass);

        ComputeHelper.Dispatch(SpatialCompute, NumBottomLayerNodes, 0);
    }

    public void UpdateOctree()
    {
        for (int i = 0; i < NumOfLayers; i++)
        {
            int layer = (int)NumOfLayers - i;
            SpatialCompute.SetInt("currentLayer", layer);
            SpatialCompute.SetInt("currentLayerStartIndex", ((int)IntPow(8, (uint)(layer - 1)) - 1) / 7);
            SpatialCompute.SetInt("numNodesInCurrentLayer", (int)IntPow(8, (uint)(layer-1)));
            ComputeHelper.Dispatch(SpatialCompute, NumOfNodes, 1);
        }
    }

    private OctreeNode CreateRootNode(float minSize)
    {
        OctreeNode rootNode = new OctreeNode();
        rootNode.centre = Vector3.zero;
        rootNode.size = minSize * IntPow(2, NumOfLayers - 1);
        rootNode.hasChildren = 1;
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

            uint currIndex = 0;
            for (uint i = 0; i < NumOfLayers; i++)
            {
                DrawLeafGizmo(Octree[currIndex].FirstChildIndex);
                currIndex = Octree[currIndex].FirstChildIndex;
            }
        }

        void DrawLeafGizmo(uint i)
        {
            Gizmos.DrawWireCube(Octree[i].centre, new Vector3(Octree[i].size, Octree[i].size, Octree[i].size));
        }

        if (Octree == null)
        {
            Gizmos.color = Color.white;
            float boundSize = 1.5f * IntPow(2, NumOfLayers - 1);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(boundSize, boundSize, boundSize));
        }
    }
}

public struct OctreeNode
{
    public float mass;
    public Vector3 CentreOfMass;
    public Vector3 centre;
    public float size;
    public uint hasChildren;
    public uint FirstChildIndex;
    public uint ParentIndex;
    public uint SpatialStartIndex;
}
