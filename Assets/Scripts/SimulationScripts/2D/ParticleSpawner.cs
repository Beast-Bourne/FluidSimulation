using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class ParticleSpawner : MonoBehaviour
{
    public int particleCount;

    public Vector2 spawnCentre;
    public Vector2 spawnSize;

    public struct ParticleSpawnData
    {
        public float2[] positions;
        public float2[] velocities;

        // Constructor
        public ParticleSpawnData(int num)
        {
            positions = new float2[num];
            velocities = new float2[num];
        }
    }

    // returns the spawn data for the particles to start at
    public ParticleSpawnData GetSpawnData()
    {
        ParticleSpawnData data = new ParticleSpawnData(particleCount);

        int numX = Mathf.CeilToInt(Mathf.Sqrt(spawnSize.x / spawnSize.y * particleCount + (spawnSize.x - spawnSize.y)*(spawnSize.x - spawnSize.y) / (4 * spawnSize.y * spawnSize.y)) - (spawnSize.x - spawnSize.y) / (2*spawnSize.y));
        int numY = Mathf.CeilToInt(particleCount / (float)numX);
        int particleNum = 0;

        for (int y = 0; y < numY; y++)
        {
            for (int x = 0; x < numX; x++)
            {
                if (particleNum >= particleCount) break;

                float tx = numX <= 1 ? 0.5f : x / (float)(numX - 1);
                float ty = numY <= 1 ? 0.5f : y / (float)(numY - 1);
                data.positions[particleNum] = new Vector2((tx-0.5f) * spawnSize.x, (ty - 0.5f) * spawnSize.y) + spawnCentre;
                data.velocities[particleNum] = Vector2.zero;

                particleNum++;
            }
        }

        return data;
    }

    // draws the bounds of the spawn area in the scene view
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(spawnCentre, spawnSize);
        }
    }
}
