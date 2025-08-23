using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[ExecuteInEditMode]
public class CelestialBodySim : MonoBehaviour
{
    public CelestialBodyInfo[] celestialBodyInfo;
    public CelestialBody[] celestialBodies;
    const float G = 2;

    public OrbitTrajectory[] orbitTrajectory;
    public bool showOrbits;
    public int orbitPredictionSteps;
    public float predictionDeltaTime;

    public CelestialBody[] InitialiseCelestialBodies()
    {
        CelestialBody[] bodyArray = new CelestialBody[celestialBodyInfo.Length];
        Vector3 totalSystemMomentum = Vector3.zero;

        for (int i = 0; i < celestialBodyInfo.Length; i++)
        {
            int parentIndex = celestialBodyInfo[i].parentBodyIndex;
            Vector3 initialVelocity = bodyArray[i].velocity + celestialBodyInfo[i].relativeVelocity + RetrieveParentVelocity(parentIndex, i);
            if (parentIndex >=0) bodyArray[parentIndex].velocity -= celestialBodyInfo[i].relativeVelocity * celestialBodyInfo[i].mass / celestialBodyInfo[parentIndex].mass;


            bodyArray[i].position = celestialBodyInfo[i].initialPosition;
            bodyArray[i].velocity = initialVelocity;
            bodyArray[i].radius = celestialBodyInfo[i].radius;
            bodyArray[i].mass = celestialBodyInfo[i].mass;
        }

        for (int i = 0; i < celestialBodyInfo.Length; i++)
        {
            if (celestialBodyInfo[i].parentBodyIndex < 0)
            {
                bodyArray[i].velocity -= totalSystemMomentum / bodyArray[i].mass;
                break;
            }
            else
            {
                continue;
            }
        }

        return bodyArray;


        Vector3 RetrieveParentVelocity(int parentIndex, int currentIndex)
        {
            if (parentIndex < 0)
            {
                return Vector3.zero;
            }

            if (celestialBodyInfo[parentIndex].parentBodyIndex < 0)
            {
                return celestialBodyInfo[parentIndex].relativeVelocity;
            }

            totalSystemMomentum += celestialBodyInfo[parentIndex].relativeVelocity * celestialBodyInfo[currentIndex].mass;
            return celestialBodyInfo[parentIndex].relativeVelocity + RetrieveParentVelocity(celestialBodyInfo[parentIndex].parentBodyIndex, currentIndex);
        }
    }

    // Calculates the acceleration due to gravity on each celestial body and applies it to update their velocities and positions
    public void GravitySimStep(Span<CelestialBody> bodies, float deltaTime)
    {
        Span<Vector3> accels = stackalloc Vector3[bodies.Length];

        for (int i = 0; i < bodies.Length-1; i++)
        {
            for (int j = i + 1; j < bodies.Length; j++)
            {
                float mass1 = bodies[i].mass;
                float mass2 = bodies[j].mass;
                
                Vector3 bodyOffset = bodies[i].position - bodies[j].position;
                float sqrDist = bodyOffset.sqrMagnitude;
                float gravForce = G * mass1 * mass2 / sqrDist;

                Vector3 dir = bodyOffset.normalized;
                Vector3 accel1 = -dir * (gravForce / mass1);
                Vector3 accel2 = dir * (gravForce / mass2);

                accels[i] += accel1;
                accels[j] += accel2;
            }
        }

        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].velocity += accels[i] * deltaTime;
            bodies[i].position += bodies[i].velocity * deltaTime;
        }
    }

    private void Update()
    {
        for (int i = 0; i < orbitTrajectory.Length; i++)
        {
            orbitTrajectory[i].lineRenderer.enabled = showOrbits;
        }

        if (Application.isPlaying || !showOrbits || celestialBodyInfo == null || celestialBodyInfo.Length != orbitTrajectory.Length) return;

        Span<CelestialBody> orbitPredictions = stackalloc CelestialBody[celestialBodyInfo.Length];


        for (int i = 0; i < celestialBodyInfo.Length; i++)
        {
            CelestialBody[] bodyArray = InitialiseCelestialBodies();
            orbitPredictions[i].position = bodyArray[i].position;
            orbitPredictions[i].velocity = bodyArray[i].velocity;
            orbitPredictions[i].radius = bodyArray[i].radius;
            orbitPredictions[i].mass = bodyArray[i].mass;


            orbitTrajectory[i].origin.position = celestialBodyInfo[i].initialPosition;
            orbitTrajectory[i].lineRenderer.positionCount = orbitPredictionSteps;
            orbitTrajectory[i].ClearTrajPoints();
        }

        for (int i = 0; i < orbitPredictionSteps; i++)
        {
            GravitySimStep(orbitPredictions, predictionDeltaTime);

            for (int j = 0; j < orbitPredictions.Length; j++)
            {
                orbitTrajectory[j].AddTrajPoint(orbitPredictions[j].position);
            }
        }

        for (int i = 0; i < orbitPredictions.Length; i++)
        {
            orbitTrajectory[i].RenderTrajectory();
            orbitTrajectory[i].lineRenderer.sharedMaterial.color = celestialBodyInfo[i].bodyColour;
        }
    }

    // draws the celestial bodies into the scene view as wire spheres
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            foreach (CelestialBodyInfo body in celestialBodyInfo)
            {
                Gizmos.color = body.bodyColour;
                Gizmos.DrawWireSphere(body.initialPosition, body.radius);
            }
        }
        else
        {
            for (int i = 0; i < celestialBodies.Length; i++)
            {
                Gizmos.color = celestialBodyInfo[i].bodyColour;
                Gizmos.DrawWireSphere(celestialBodies[i].position, celestialBodies[i].radius);
            }
        }
    }
}

public struct CelestialBody
{
    public Vector3 position;
    public Vector3 velocity;
    public float radius;
    public float mass;
}

[System.Serializable]
public struct CelestialBodyInfo
{
    private CelestialBody body;
    public Vector3 initialPosition;
    public Vector3 relativeVelocity;
    public float radius;
    public float mass;
    public int parentBodyIndex;
    public Color bodyColour;
}

[System.Serializable]
public class OrbitTrajectory
{
    public Transform origin;
    public LineRenderer lineRenderer;
    public bool applyScale;

    List<Vector3> trajectoryPoints = new();

    public void ClearTrajPoints()
    {
        trajectoryPoints.Clear();
    }

    public void AddTrajPoint(Vector3 point)
    {
        if (trajectoryPoints.Count == 0 || (point - trajectoryPoints[^1]).sqrMagnitude > 0.05f) // trajectoryPoints[^1] get the last element in the list
        {
            trajectoryPoints.Add(point);
        }
    }

    public void RenderTrajectory()
    {
        lineRenderer.positionCount = trajectoryPoints.Count;
        for (int i = 0; i < trajectoryPoints.Count; i++)
        {
            lineRenderer.SetPosition(i, trajectoryPoints[i]);
        }
    }
}