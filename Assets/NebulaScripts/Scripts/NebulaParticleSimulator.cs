using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class NebulaParticleSimulator : MonoBehaviour
{
    public event System.Action SimulationStepFinished;

    [Header("References")]
    public NebulaParticleSpawner spawner;
    public NebulaParticleDisplay display;
    public OctreeManager octreeManager;
    public ComputeShader compute;

    // buffers for the compute shader
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;
    ComputeBuffer OctreeBuffer;
    ComputeBuffer SpatialHashes;
    BufferSorter sorter;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public float gravity;
    public float particleMass;
    public float damping;
    public float smoothingRadius = 2;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscocityMultiplier;
    public float gasConstant;
    public float adiabaticIndex;
    public bool usePredictions;

    // kernel IDs for the compute shader
    const int UpdatePredictionsKernel = 0;
    const int gridHashKernel = 1;
    const int octreeKernel = 2;
    const int gravityKernel = 3;
    const int densityCalculationKernel = 4;
    const int pressureForceKernel = 5;
    const int viscosityKernel = 6;
    const int updatePositionKernel = 7;

    // other
    public bool isPaused;
    bool isPausedNextFrame;
    NebulaParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }

    // Initialisation. Gets the spawn data from the spawner and sets the initial buffer data before telling the display to initialise
    private void Start()
    {
        spawnData = spawner.GetSpawnData();
        particleCount = spawnData.positions.Length;

        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
        OctreeBuffer = ComputeHelper.CreateStructuredBuffer<OctreeNode>(octreeManager.NumOfNodes);
        SpatialHashes = ComputeHelper.CreateStructuredBuffer<uint>(octreeManager.NumBottomLayerNodes*8);

        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", UpdatePredictionsKernel, gridHashKernel, updatePositionKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", updatePositionKernel, gridHashKernel, densityCalculationKernel, pressureForceKernel, UpdatePredictionsKernel, viscosityKernel, gravityKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel, octreeKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel, octreeKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", UpdatePredictionsKernel, updatePositionKernel, pressureForceKernel, viscosityKernel, gravityKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityCalculationKernel, pressureForceKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, OctreeBuffer, "Octree", gravityKernel, octreeKernel);
        ComputeHelper.SetBuffer(compute, SpatialHashes, "SpatialHashes", gravityKernel, octreeKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(spatialIndices, spatialOffsets);

        octreeManager.SetBuffers(OctreeBuffer, SpatialHashes, smoothingRadius*2.0f);

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

    // Runs the simulation step in fixed time intervals if the fixedTimeStep variable is set to true
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
        if (Time.frameCount < 10) return; // skip first few frames to avoid a disproportionate delta time

        UpdateComputeSettings(frameTime);
        RunSimulationStep();
        SimulationStepFinished?.Invoke(); // the '?' is a null-conditional operator (won't run the event if there is no subscribed action)
    }

    // Dispatches the compute shader to run the simulation step
    void RunSimulationStep()
    {
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel); // first update the predicted postions
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel); // then calculate the spatial hashes and indices
        sorter.SortAndCalcOffsets(); // sort the indices and calculate the offsets
        ComputeHelper.Dispatch(compute, particleCount, octreeKernel); // update the octree mass values
        ComputeHelper.Dispatch(compute, particleCount, gravityKernel); // apply gravity between particles
        ComputeHelper.Dispatch(compute, particleCount, densityCalculationKernel); // calculate the density at each particle
        ComputeHelper.Dispatch(compute, particleCount, pressureForceKernel); // calculate and apply pressure forces
        ComputeHelper.Dispatch(compute, particleCount, viscosityKernel); // calculate and apply viscocity forces
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel); // finally update the particle positions and velocities
    }

    // Updates the compute settings for the simulation that are intended to be changeable during runtime
    void UpdateComputeSettings(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("particleMass", particleMass);
        compute.SetVector("boundSize", new Vector3(octreeManager.BoundSize, octreeManager.BoundSize, octreeManager.BoundSize));
        compute.SetFloat("damping", damping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetBool("usePredictions", usePredictions);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);
        compute.SetFloat("gasConstant", gasConstant);
        compute.SetFloat("adiabaticIndex", adiabaticIndex);

        compute.SetFloat("Pow2Factor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow2DerivativeFactor", 12 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow3Factor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("Pow3DerivativeFactor", 30 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("PolynomialPow6Factor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(NebulaParticleSpawner.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
    }

    // Handles user input to pause the simulation, reset it, or run a single frame
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
        ComputeHelper.Release(positionBuffer, velocityBuffer, densityBuffer, predictedPositionBuffer, spatialIndices, spatialOffsets, OctreeBuffer, SpatialHashes);
    }
}
