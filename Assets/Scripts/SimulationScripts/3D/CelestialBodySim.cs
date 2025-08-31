using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

[ExecuteInEditMode]
public class CelestialBodySim : MonoBehaviour
{
    public CelestialBodyInfo[] celestialBodyInfo;
    public CelestialBody[] celestialBodies;
    const float G = 1;

    public OrbitTrajectory[] orbitTrajectory;
    public bool showOrbits;
    public int orbitPredictionSteps;
    public float predictionDeltaTime;

    public CelestialBody[] InitialiseCelestialBodies()
    {
        CelestialBody[] bodyArray = new CelestialBody[celestialBodyInfo.Length];

        Vector3 sunPosition = Vector3.zero;
        Vector3 planetPosition = new Vector3(celestialBodyInfo[0].orbitalRadius, 0, 0);
        Vector3 moonPosition = planetPosition + new Vector3(celestialBodyInfo[1].orbitalRadius, 0, 0);

        Vector3 sunVelocity = Vector3.zero;
        Vector3 planetVelocity = Vector3.zero;
        Vector3 moonVelocity = Vector3.zero;

        float sunMass = celestialBodyInfo[2].mass;
        float planetMass = celestialBodyInfo[0].mass;
        float moonMass = celestialBodyInfo[1].mass;

        float planetSunSeparation = celestialBodyInfo[0].orbitalRadius;
        float moonPlanetSeparation = celestialBodyInfo[1].orbitalRadius;
        float moonSunSeparation = planetSunSeparation + moonPlanetSeparation;
        float pmBarycentreMoonSeparation = (planetMass / (planetMass + moonMass)) * moonPlanetSeparation;
        float pmBarycentrePlanetSeparation = (moonMass / (planetMass + moonMass)) * moonPlanetSeparation;
        float sunPMBarycentreSeparation = planetSunSeparation + pmBarycentrePlanetSeparation;
        float systemBarycentreSunSeparation = ((planetMass + moonMass) / (sunMass + planetMass + moonMass)) * (pmBarycentrePlanetSeparation + planetSunSeparation);
        float pmBarycentreSystemSeparation = sunPMBarycentreSeparation - systemBarycentreSunSeparation;

        float a = pmBarycentreSystemSeparation;
        float rm = pmBarycentreMoonSeparation;
        float rp = pmBarycentrePlanetSeparation;

        float pmbVelAroundSunMag = Mathf.Sqrt(G * (sunMass + planetMass + moonMass) / (a));
        Vector3 pmbVelAroundSun = Vector3.forward * pmbVelAroundSunMag * (sunMass / (sunMass + planetMass + moonMass));

        float moonVelAroundPMBMag = Mathf.Sqrt((G * (planetMass) / (moonPlanetSeparation)) - (0.5f * G * sunMass * (moonPlanetSeparation * moonPlanetSeparation)/ (planetSunSeparation * planetSunSeparation * planetSunSeparation)));
        Vector3 moonRelVel = Vector3.up * moonVelAroundPMBMag;
        Vector3 planetRelVel = -Vector3.up * (moonMass / (planetMass + moonMass)) * moonVelAroundPMBMag;
        moonRelVel *= (planetMass / (planetMass + moonMass));

        moonVelocity = pmbVelAroundSun + moonRelVel;
        planetVelocity = pmbVelAroundSun + planetRelVel;

        Vector3 totalMomentum = moonMass * moonVelocity + planetMass * planetVelocity;
        sunVelocity = -totalMomentum / sunMass;

        bodyArray[0].mass = planetMass;
        bodyArray[0].radius = celestialBodyInfo[0].radius;
        bodyArray[0].position = planetPosition;
        bodyArray[0].velocity = planetVelocity;

        bodyArray[1].mass = moonMass;
        bodyArray[1].radius = celestialBodyInfo[1].radius;
        bodyArray[1].position = moonPosition;
        bodyArray[1].velocity = moonVelocity;

        bodyArray[2].mass = sunMass;
        bodyArray[2].radius = celestialBodyInfo[2].radius;
        bodyArray[2].position = sunPosition;
        bodyArray[2].velocity = sunVelocity;

        return bodyArray;
        /*
        bodyArray[0].mass = celestialBodyInfo[0].mass;
        bodyArray[0].radius = celestialBodyInfo[0].radius;
        bodyArray[1].mass = celestialBodyInfo[1].mass;
        bodyArray[1].radius = celestialBodyInfo[1].radius;
        bodyArray[2].mass = celestialBodyInfo[2].mass;
        bodyArray[2].radius = celestialBodyInfo[2].radius;

        bodyArray[2].position = Vector3.zero;
        bodyArray[0].position = bodyArray[2].position + Vector3.right * celestialBodyInfo[0].orbitalRadius;
        bodyArray[1].position = bodyArray[0].position + (Vector3.right * celestialBodyInfo[1].orbitalRadius);


        float moonMass = celestialBodyInfo[1].mass;
        float planetMass = celestialBodyInfo[0].mass;
        float sunMass = celestialBodyInfo[2].mass;
        float planetMoonBaryCentrePos = (planetMass * celestialBodyInfo[0].orbitalRadius + moonMass * (celestialBodyInfo[1].orbitalRadius + celestialBodyInfo[0].orbitalRadius))/(planetMass+moonMass);
        float sunPlanetBaryCentrePos = ((planetMass + moonMass) * planetMoonBaryCentrePos) / (sunMass + planetMass + moonMass);

        float a = planetMoonBaryCentrePos;// - sunPlanetBaryCentrePos;
        float r = celestialBodyInfo[1].orbitalRadius + celestialBodyInfo[0].orbitalRadius - planetMoonBaryCentrePos;

        float sunFactor = G * (planetMass + sunMass) / (a * a * a);
        Vector3 moonVel = Vector3.up * Mathf.Sqrt((G * (planetMass) / r) + 1.5f * sunFactor * r * r);
        bodyArray[1].velocity = moonVel * planetMass / (planetMass + moonMass);
        bodyArray[0].velocity = -moonVel * moonMass / (planetMass + moonMass);

        Vector3 planetVel = Vector3.forward * Mathf.Sqrt(G * (sunMass + moonMass + planetMass) / a);
        planetVel *= sunMass / (moonMass + planetMass + sunMass);

        bodyArray[0].velocity += planetVel;
        bodyArray[1].velocity += planetVel;

        bodyArray[2].velocity = Vector3.zero;
        bodyArray[2].velocity -= planetVel * (moonMass + planetMass) / (moonMass + planetMass + sunMass);
        
        return bodyArray;
        */
    }

    // Calculates the acceleration due to gravity on each celestial body and applies it to update their velocities and positions
    // Span is used to avoid heap allocations for performance (its essentially a stack-allocated array)
    // stackalloc is used to allocate memory on the stack which is faster than heap allocation (stack has a few MB of memory so it can be used for small arrays)
    public void GravitySimStep(Span<CelestialBody> bodies, float deltaTime)
    {
        Span<Vector3> accels = stackalloc Vector3[bodies.Length];

        ComputeCelestialAccels(bodies, accels);

        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].velocity += accels[i] * deltaTime * 0.5f;
            bodies[i].position += bodies[i].velocity * deltaTime;
        }

        accels.Clear();
        ComputeCelestialAccels(bodies, accels);

        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].velocity += accels[i] * deltaTime * 0.5f;
        }


        Vector3 totalMomentum = Vector3.zero;
        float totalMass = 0.0f;
        for (int i = 0; i < bodies.Length; i++)
        {
            totalMomentum += bodies[i].mass * bodies[i].velocity;
            totalMass += bodies[i].mass;
        }

        Vector3 driftVelocity = totalMomentum / totalMass;
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].velocity -= driftVelocity;
        }

        void ComputeCelestialAccels(Span<CelestialBody> bodies, Span<Vector3> accels)
        {
            float minDist = 0.1f; // to avoid division by zero in the gravitational force calculation

            for (int i = 0; i < bodies.Length - 1; i++)
            {
                for (int j = i + 1; j < bodies.Length; j++)
                {
                    float mass1 = bodies[i].mass;
                    float mass2 = bodies[j].mass;

                    Vector3 bodyOffset = bodies[i].position - bodies[j].position;
                    float sqrDist = bodyOffset.sqrMagnitude + minDist*minDist;

                    float test = 1.0f / (Mathf.Sqrt(sqrDist) * sqrDist);
                    Vector3 accel1 = -G * mass2 * bodyOffset * test;
                    Vector3 accel2 = G * mass1 * bodyOffset * test;


                    //Vector3 dir = bodyOffset.normalized;
                    //Vector3 accel1 = -dir * (G * mass2 / sqrDist);
                    //Vector3 accel2 = dir * (G * mass1 / sqrDist);

                    accels[i] += accel1;
                    accels[j] += accel2;
                }
            }
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
            orbitTrajectory[i].lineRenderer.startColor = celestialBodyInfo[i].bodyColour;
            orbitTrajectory[i].lineRenderer.endColor = celestialBodyInfo[i].bodyColour;
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
    public float orbitalRadius;
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