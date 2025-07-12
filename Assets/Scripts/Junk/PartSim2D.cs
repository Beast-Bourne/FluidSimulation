using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class PartSim2D : MonoBehaviour
{
    [Header("References")]
    public PartDisp2D partDisp;
    public ParticleSpawner spawner;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public Vector2 boundSize;
    public float gravity;
    public float damping;
    public float smoothingRadius = 2;
    public float targetDensity;
    public float pressureMultiplier;

    // buffers
    [NonSerialized]
    public float2[] positions;
    [NonSerialized]
    public float2[] velocities;
    [NonSerialized]
    public float[] densities;

    public bool isPaused;
    ParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }
    private float simulationTime;

    private void Start()
    {
        spawnData = spawner.GetSpawnData();
        particleCount = spawnData.positions.Length;

        positions = spawnData.positions;
        velocities = spawnData.velocities;
        densities = new float[particleCount];

        partDisp.Init(this);
    }

    private void Update()
    {
        if (!fixedTimeStep)
        {
            RunSimFrame(Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (fixedTimeStep)
        {
            RunSimFrame(Time.fixedDeltaTime * timeScale);
        }
    }

    private void RunSimFrame(float timeStep)
    {
        if (isPaused) return;
        if (Time.frameCount <= 10) return;

        simulationTime = timeStep;
        RunSimStep();
    }

    private void RunSimStep()
    {
        for (uint i = 0; i < particleCount; i++)
        {
            ApplyExternalForces(i);
            CalculateDensity(i);
            CalculatePressureForce(i);
            UpdatePosition(i);
        }

        partDisp.MoveParticles();
    }

    private void HandleUserInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            positions = spawnData.positions;
            velocities = spawnData.velocities;
            RunSimFrame(0);
            positions = spawnData.positions;
            velocities = spawnData.velocities;
        }
    }

    float SmoothingKernel(float dist)
    {
        if (dist > smoothingRadius) return 0.0f;

        float volume = math.PI * Mathf.Pow(smoothingRadius, 4)/6;
        return (smoothingRadius - dist) * (smoothingRadius - dist) / volume;
    }

    float DerivativeSmoothingKernel(float dist)
    {
        if (dist > smoothingRadius) return 0.0f;

        float scale = 12/ (math.PI * Mathf.Pow(smoothingRadius, 4));
        return (dist - smoothingRadius) / scale;
    }

    void ApplyExternalForces(uint particleIndex)
    {
        velocities[particleIndex] += new float2(0, gravity * simulationTime);
    }

    void CalculateDensity(uint particleIndex)
    {
        float density = 0;
        float2 pos = positions[particleIndex];
        float sqrRadius = smoothingRadius * smoothingRadius;

        for (uint i = 0; i < particleCount; i++)
        {
            float2 offset = positions[i] - pos;
            float sqrDist = math.dot(offset, offset);
            if (sqrDist > sqrRadius) continue;

            float dist = Mathf.Sqrt(sqrDist);
            density += SmoothingKernel(dist);
        }

        densities[particleIndex] = density;
    }

    void CalculatePressureForce(uint particleIndex)
    {
        float density = densities[particleIndex];
        float pressure = pressureMultiplier * (density - targetDensity);
        float2 pressureForce = float2.zero;

        float2 pos = positions[particleIndex];
        float sqrRadius = smoothingRadius * smoothingRadius;

        uint index = 0;
        while (index < particleCount)
        {
            index++;
            if (index == particleIndex) continue;

            float2 offset = positions[index] - pos;
            float sqrDist = math.dot(offset, offset);
            if (sqrDist > sqrRadius) continue;

            float dist = Mathf.Sqrt(sqrDist);
            float2 dirToParticle = (dist > 0) ? offset / dist : new float2(0, 1);

            float particleDensity = densities[index];
            float particlePressure = pressureMultiplier * (particleDensity - targetDensity);

            float sharedPressure = (pressure + particlePressure) * 0.5f;

            pressureForce += DerivativeSmoothingKernel(dist) * dirToParticle * sharedPressure / particleDensity;
        }

        float2 accel = pressureForce / density;
        velocities[particleIndex] += accel * simulationTime;
    }

    void UpdatePosition(uint particleIndex)
    {
        positions[particleIndex] += velocities[particleIndex] * simulationTime;
        
        float2 pos = positions[particleIndex];
        float2 vel = velocities[particleIndex];

        Vector2 halfSize = boundSize * 0.5f;
        Vector2 edgDist = halfSize - new Vector2(math.abs(pos.x), math.abs(pos.y));

        if (edgDist.x < 0)
        {
            pos.x = halfSize.x * math.sign(pos.x);
            vel.x *= -damping;
        }
        if (edgDist.y < 0)
        {
            pos.y = halfSize.y * math.sign(pos.y);
            vel.y *= -damping;
        }

        positions[particleIndex] = pos;
        velocities[particleIndex] = vel;
    }
}
