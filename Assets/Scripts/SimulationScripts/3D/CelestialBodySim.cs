using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class CelestialBodySim : MonoBehaviour
{
    public CelestialBody[] celestialBodies;
    const float G = 3;

    // Calculates the acceleration due to gravity on each celestial body and applies it to update their velocities and positions
    public void GravitySimStep(float deltaTime)
    {
        Vector3[] accels = new Vector3[celestialBodies.Length];

        for (int i = 0; i < celestialBodies.Length-1; i++)
        {
            for (int j = i + 1; j < celestialBodies.Length; j++)
            {
                float mass1 = celestialBodies[i].mass;
                float mass2 = celestialBodies[j].mass;
                
                Vector3 bodyOffset = celestialBodies[i].position - celestialBodies[j].position;
                float sqrDist = bodyOffset.sqrMagnitude;
                float gravForce = G * mass1 * mass2 / sqrDist;

                Vector3 dir = bodyOffset.normalized;
                Vector3 accel1 = -dir * (gravForce / mass1);
                Vector3 accel2 = dir * (gravForce / mass2);

                accels[i] += accel1;
                accels[j] += accel2;
            }
        }

        for (int i = 0; i < celestialBodies.Length; i++)
        {
            celestialBodies[i].velocity += accels[i] * deltaTime;
            celestialBodies[i].position += celestialBodies[i].velocity * deltaTime;
        }
    }

    // draws the celestial bodies into the scene view as wire spheres
    private void OnDrawGizmos()
    {
        foreach (CelestialBody body in celestialBodies)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(body.position, body.radius);
        }
    }
}

[System.Serializable]
public struct CelestialBody
{
    public Vector3 position;
    public Vector3 velocity;
    public float radius;
    public float mass;
}