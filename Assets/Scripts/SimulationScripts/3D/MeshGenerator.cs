using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MeshGenerator
{
    static readonly int[] vertices = { 0, 1, 0, 2, 0, 3, 0, 4, 1, 2, 2, 3, 3, 4, 4, 1, 5, 1, 5, 2, 5, 3, 5, 4 };
    static readonly int[] triangles = { 0, 1, 4, 1, 2, 5, 2, 3, 6, 3, 0, 7, 8, 9, 4, 9, 10, 5, 10, 11, 6, 11, 8, 7 };
    static readonly Vector3[] initVerts = { Vector3.up, Vector3.left, Vector3.back, Vector3.right, Vector3.forward, Vector3.down };

    public static Mesh ReturnSphere(int res)
    {
        Mesh mesh = new Mesh();
        int numDivisions = Mathf.Max(0, res);
        int temp = numDivisions + 3;
        int vertsPerFace = (temp * temp - temp) / 2;
        int totalVerts = (vertsPerFace * 8) - (numDivisions + 2) * 12 + 6;
        int trisPerFace = (numDivisions + 1) * (numDivisions + 1);

        var verts = new FixedLengthList<Vector3>(totalVerts);
        var tris = new FixedLengthList<int>(trisPerFace * 24);

        verts.AddRange(initVerts);
        Edge[] edges = new Edge[12];

        // This loop creates the edges of the sphere
        for (int i = 0; i < vertices.Length; i += 2)
        {
            Vector3 initVert = verts.values[vertices[i]];
            Vector3 finVert = verts.values[vertices[i + 1]];

            int[] edgeIndices = new int[numDivisions + 2];
            edgeIndices[0] = vertices[i];

            for (int j = 0; j < numDivisions; j++)
            {
                float t = (j + 1.0f) / (numDivisions + 1.0f);
                edgeIndices[j+1] = verts.nextIndex;
                verts.Add(Vector3.Slerp(initVert, finVert, t));
            }

            edgeIndices[numDivisions + 1] = vertices[i + 1];
            int index = i / 2;
            edges[index] = new Edge(edgeIndices);
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int index = i / 3;
            bool flipOrder = index >= 4;
            AddTriangle(edges[triangles[i]], edges[triangles[i + 1]], edges[triangles[i + 2]], flipOrder);
        }

        mesh.SetVertices(verts.values);
        mesh.SetTriangles(tris.values, 0, true);
        mesh.RecalculateNormals();
        return mesh;


        // local function
        void AddTriangle(Edge sideA, Edge sideB, Edge bottom, bool flipOrder)
        {
            int totalEdgeDivisions = sideA.vertIndices.Length;
            var vertMap = new FixedLengthList<int>(vertsPerFace);
            vertMap.Add(sideA.vertIndices[0]); // top of the tri

            for (int i = 1; i < totalEdgeDivisions - 1; i++)
            {
                vertMap.Add(sideA.vertIndices[i]);

                Vector3 sideAVert = verts.values[sideA.vertIndices[i]];
                Vector3 sideBVert = verts.values[sideB.vertIndices[i]];
                int numOfInnerVerts = i - 1;

                for (int j = 0; j < numOfInnerVerts; j++)
                {
                    float temp = (j + 1.0f) / (numOfInnerVerts + 1.0f);
                    vertMap.Add(verts.nextIndex);
                    verts.Add(Vector3.Slerp(sideAVert, sideBVert, temp));
                }

                vertMap.Add(sideB.vertIndices[i]);
            }

            // Add the bottom edge vertices
            for (int i = 0; i < totalEdgeDivisions; i++)
            {
                vertMap.Add(bottom.vertIndices[i]);
            }

            // form the triangles
            int totalRows = numDivisions + 1;
            for (int i = 0; i < totalRows; i++)
            {
                int topVert = ((i + 1) * (i + 1) - i - 1) / 2;
                int bottomVert = ((i + 2) * (i + 2) - i - 2) / 2;
                int trisPerRow = 1 + 2 * i;

                for (int j = 0; j < trisPerRow; j++)
                {
                    int v0, v1, v2;

                    if (j % 2 == 0)
                    {
                        v0 = topVert;
                        v1 = topVert + 1;
                        v2 = bottomVert;
                        topVert++;
                        bottomVert++;
                    }
                    else
                    {
                        v0 = topVert;
                        v1 = bottomVert;
                        v2 = topVert - 1;
                    }

                    tris.Add(vertMap.values[v0]);
                    tris.Add(vertMap.values[(flipOrder) ? v2 : v1]);
                    tris.Add(vertMap.values[(flipOrder) ? v1 : v2]);
                }
            }
        }
    }
}

public struct Edge
{
    public int[] vertIndices;

    public Edge(int[] vertIndices)
    {
        this.vertIndices = vertIndices;
    }
}

public struct FixedLengthList<T>
{
    public T[] values;
    public int nextIndex;

    public FixedLengthList(int length)
    {
        values = new T[length];
        nextIndex = 0;
    }

    public void Add(T value)
    {
        values[nextIndex] = value;
        nextIndex++;
    }

    public void AddRange(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            Add(value);
        }
    }
}
