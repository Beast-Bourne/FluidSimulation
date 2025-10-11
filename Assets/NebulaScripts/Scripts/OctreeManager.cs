using UnityEngine;

public class OctreeManager : MonoBehaviour
{
    [SerializeField]
    private bool DisplayOctree;

    public TreeNode Octree;

    public static Vector3[] childOffsets = 
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

    public void BuildOctree(float rootSize, float minSize)
    {
        Octree = new TreeNode(Vector3.zero, rootSize, minSize);
    }

    private void OnDrawGizmos()
    {
        if (DisplayOctree && Octree != null)
        {
            Octree.DrawNodeGizmos();
        }
    }
}

public class TreeNode
{
    public Vector3 centre;
    public float size;
    public TreeNode[] children;
    public TreeNode parent;
    public float minSize;
    public bool hasChildren;


    public TreeNode(Vector3 centre, float size, float minSize, TreeNode parent = null)
    {
        this.centre = centre;
        this.size = size;
        this.parent = parent;
        children = new TreeNode[8];
        this.minSize = minSize;
        hasChildren = false;

        InitialiseChildren();
    }

    public void InitialiseChildren()
    {
        if (size / 2 < minSize) return;

        hasChildren = true;
        float childSize = size / 2;
        float childCentreOffset = childSize / 2;

        for (int i = 0; i < 8; i++)
        {
            Vector3 childCentre = centre + OctreeManager.childOffsets[i] * childCentreOffset;
            children[i] = new TreeNode(childCentre, childSize, minSize, this);
        }

    }

    public void DrawNodeGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(centre, new Vector3(size, size, size));

        if (hasChildren)
        {
            foreach (TreeNode child in children)
            {
                child.DrawNodeGizmos();
            }
        }
    }
}
