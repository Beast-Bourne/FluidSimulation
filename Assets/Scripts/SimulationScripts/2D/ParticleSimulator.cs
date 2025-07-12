using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class ParticleSimulator : MonoBehaviour
{
    public event System.Action SimulationStepFinished;

    [Header("References")]
    public ParticleSpawner spawner;
    public ParticleDisplay2D display;
    public ComputeShader compute;

    // buffers for the compute shader
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;
    BufferSorter sorter;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public Vector2 boundSize;
    public float gravity;
    public float damping;
    public float smoothingRadius = 2;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscocityMultiplier;
    public float mouseForceStrength;
    public float mouseRadius;
    public bool usePredictions;

    // kernel IDs for the compute shader
    const int externalForceKernel = 0;
    const int gridHashKernel = 1;
    const int densityCalculationKernel = 2;
    const int pressureForceKernel = 3;
    const int viscosityKernel = 4;
    const int updatePositionKernel = 5;

    // other
    public bool isPaused;
    bool isPausedNextFrame;
    ParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }

    // Initialisation. Gets the spawn data from the spawner and sets the initial buffer data before telling the display to initialise
    private void Start()
    {
        spawnData = spawner.GetSpawnData();
        particleCount = spawnData.positions.Length;

        positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);

        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForceKernel, updatePositionKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", updatePositionKernel, gridHashKernel, densityCalculationKernel, pressureForceKernel, externalForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForceKernel, updatePositionKernel, pressureForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityCalculationKernel, pressureForceKernel, viscosityKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(spatialIndices, spatialOffsets);

        display.Init(this);
    }

    // Runs the simulation frame by frame
    private void Update()
    {
        if (!fixedTimeStep)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (isPausedNextFrame)
        {
            isPaused = true;
            isPausedNextFrame = false;
        }

        HandleUserImput();
    }

    private void FixedUpdate()
    {
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime * timeScale);
        }
    }

    // updates the compute settings and runs the simulation step. Flags an event to say the simulation step has finished
    void RunSimulationFrame(float frameTime)
    {
        if (isPaused) return;
        if (Time.frameCount < 10) return; // skip first few frames to avoid a disporportionate delta time

        UpdateComputeSettings(frameTime);
        RunSimulationStep();
        SimulationStepFinished?.Invoke(); // the '?' is a null-conditional operator (wont run the event if there is no subscribed action)
    }

    // Dispatches the compute shader to run the simulation step
    void RunSimulationStep()
    {
        ComputeHelper.Dispatch(compute, particleCount, externalForceKernel);
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel);
        sorter.SortAndCalcOffsets();
        ComputeHelper.Dispatch(compute, particleCount, densityCalculationKernel);
        ComputeHelper.Dispatch(compute, particleCount, pressureForceKernel);
        ComputeHelper.Dispatch(compute, particleCount, viscosityKernel);
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel);
    }

    // Updates the compute settings for the simulation that are intended to be changeable during runtime
    void UpdateComputeSettings(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetVector("bounds", boundSize);
        compute.SetFloat("damping", damping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetBool("usePredictions", usePredictions);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);

        compute.SetFloat("Pow2Factor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow2DerivativeFactor", 12 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow3Factor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("Pow3DerivativeFactor", 30 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("PolynomialPow6Factor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        bool isPulling = Input.GetMouseButton(0);
        bool isPushing = Input.GetMouseButton(1);
        float forceStrength = (isPulling)? mouseForceStrength : (isPushing) ? -mouseForceStrength : 0;

        compute.SetVector("mousePos", mousePos);
        compute.SetFloat("interactionStrength", forceStrength);
        compute.SetFloat("mouseRadius", mouseRadius);
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(ParticleSpawner.ParticleSpawnData spawnData)
    {
        float2[] allPoints = new float2[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
    }

    void HandleUserImput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
            SetInitialBufferData(spawnData);
            RunSimulationStep();
            SetInitialBufferData(spawnData);
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            isPausedNextFrame = true;
        }
    }

    // Clears the memory of all the buffers from the compute shader when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(positionBuffer, velocityBuffer, densityBuffer, predictedPositionBuffer, spatialIndices, spatialOffsets);
    }

    // Draws the bounds of the fluid container in the scene
    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.green;
            Vector2 bounds = new Vector2(boundSize.x + display.scale, boundSize.y + display.scale);
            Gizmos.DrawWireCube(transform.position, bounds);
        }
    }
}
