using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class NebulaParticleSpawner : MonoBehaviour
{
    public int particlesPerSide;
    public Vector3 spawnCentre;
    public float spawnSize;

    public Vector3 startingVelocity;
    public float randomOffsetStrength;

    // This function generates the spawn data for particles in a 3D bounds
    // it applies jitter so they particles arent in a uniform grid
    public ParticleSpawnData GetSpawnData()
    {
        ParticleSpawnData data = new ParticleSpawnData(particlesPerSide * particlesPerSide * particlesPerSide);

        int particleNum = 0;

        for (int x = 0; x < particlesPerSide; x++)
        {
            for (int y = 0; y < particlesPerSide; y++)
            {
                for (int z = 0; z < particlesPerSide; z++)
                {
                    float tx = x / (particlesPerSide - 1f);
                    float ty = y / (particlesPerSide - 1f);
                    float tz = z / (particlesPerSide - 1f);

                    float px = (tx - 0.5f) * spawnSize + spawnCentre.x;
                    float py = (ty - 0.5f) * spawnSize + spawnCentre.y;
                    float pz = (tz - 0.5f) * spawnSize + spawnCentre.z;

                    float3 randOffset = UnityEngine.Random.insideUnitSphere * randomOffsetStrength;
                    data.positions[particleNum] = new float3(px, py, pz) + randOffset;
                    data.velocities[particleNum] = startingVelocity;
                    particleNum++;
                }
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
            Gizmos.DrawWireCube(spawnCentre, Vector3.one * spawnSize);
        }
    }

    public struct ParticleSpawnData
    {
        public float3[] positions;
        public float3[] velocities;

        // Constructor
        public ParticleSpawnData(int num)
        {
            positions = new float3[num];
            velocities = new float3[num];
        }
    }
}
