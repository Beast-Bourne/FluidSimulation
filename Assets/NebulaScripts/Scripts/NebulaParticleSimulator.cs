using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    public ComputeBuffer InternalEnergyBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;
    ComputeBuffer OctreeBuffer;
    ComputeBuffer SpatialHashes;
    BufferSorter sorter;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public float damping;
    public float smoothingRadius = 2;
    public float pressureMultiplier;
    public float viscocityMultiplier;
    public float gasConstant;
    public float adiabaticIndex;
    public bool usePredictions;
    public bool isPaused;

    [Header("Gravity Settings")]
    public float gravity;
    public float particleMass;
    public float barnesHutAccuracyThreshold;
    public float softeningLength;

    [Header("Initial Energy Settings")]
    public float InitialTemperature;
    public float BoltzmannConstant;
    public float ProtonMass;
    public float MeanMolecularWeight;

    [Header("Data Collection Settings")]
    public bool enableDataCollection;
    public float TimeBetweenSamples;
    public float TotalSampleTime;
    public string logFileName;

    // kernel IDs for the compute shader
    const int UpdatePredictionsKernel = 0;
    const int gridHashKernel = 1;
    const int gravityKernel = 2;
    const int densityCalculationKernel = 3;
    const int pressureForceKernel = 4;
    const int internalEnergyKernel = 5;
    const int viscosityKernel = 6;
    const int updatePositionKernel = 7;

    // other
    bool isPausedNextFrame;
    NebulaParticleSpawner.ParticleSpawnData spawnData;
    public int particleCount { get; private set; }
    float timeElapsed;
    float totalTimeElapsed;
    List<string> logData = new List<string>();

    // Initialisation. Gets the spawn data from the spawner and sets the initial buffer data before telling the display to initialise
    private void Start()
    {
        spawnData = spawner.GetSpawnData(adiabaticIndex, InitialTemperature, BoltzmannConstant, ProtonMass, MeanMolecularWeight);
        particleCount = spawnData.positions.Length;

        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(particleCount);
        OctreeBuffer = ComputeHelper.CreateStructuredBuffer<OctreeNode>(octreeManager.NumOfNodes);
        SpatialHashes = ComputeHelper.CreateStructuredBuffer<uint>(octreeManager.NumBottomLayerNodes*8);
        InternalEnergyBuffer = ComputeHelper.CreateStructuredBuffer<float>(particleCount);

        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", UpdatePredictionsKernel, gridHashKernel, updatePositionKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", updatePositionKernel, gridHashKernel, densityCalculationKernel, pressureForceKernel, UpdatePredictionsKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", gridHashKernel, densityCalculationKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", UpdatePredictionsKernel, updatePositionKernel, pressureForceKernel, viscosityKernel, gravityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityCalculationKernel, pressureForceKernel, viscosityKernel, internalEnergyKernel);
        ComputeHelper.SetBuffer(compute, OctreeBuffer, "Octree", gravityKernel);
        ComputeHelper.SetBuffer(compute, SpatialHashes, "SpatialHashes", gravityKernel);
        ComputeHelper.SetBuffer(compute, InternalEnergyBuffer, "InternalEnergies", pressureForceKernel, internalEnergyKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(spatialIndices, spatialOffsets);

        octreeManager.SetBuffers(OctreeBuffer, SpatialHashes, smoothingRadius*2.0f, spatialIndices, spatialOffsets, particleCount, positionBuffer, particleMass);

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

        if (totalTimeElapsed < TotalSampleTime && enableDataCollection)
        {
            SampleFrameRate(Time.deltaTime);
        }
        else if (enableDataCollection && totalTimeElapsed >= TotalSampleTime)
        {
            enableDataCollection = false;
            Debug.Log("Finished Data Collection");
        }
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
        octreeManager.UpdateOctree(); // update the octree mass values
        ComputeHelper.Dispatch(compute, particleCount, gravityKernel); // apply gravity between particles
        ComputeHelper.Dispatch(compute, particleCount, densityCalculationKernel); // calculate the density at each particle
        ComputeHelper.Dispatch(compute, particleCount, pressureForceKernel); // calculate and apply pressure forces
        ComputeHelper.Dispatch(compute, particleCount, internalEnergyKernel); // update the internal energies of the particles
        ComputeHelper.Dispatch(compute, particleCount, viscosityKernel); // calculate and apply viscocity forces
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel); // finally update the particle positions and velocities

    }

    // Updates the compute settings for the simulation that are intended to be changeable during runtime
    void UpdateComputeSettings(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("particleMass", particleMass);
        compute.SetFloat("BarnesHutTheta", barnesHutAccuracyThreshold);
        compute.SetFloat("softeningLength", softeningLength);
        compute.SetVector("boundSize", new Vector3(octreeManager.BoundSize, octreeManager.BoundSize, octreeManager.BoundSize));
        compute.SetFloat("damping", damping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetBool("usePredictions", usePredictions);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);
        compute.SetFloat("gasConstant", gasConstant);
        compute.SetFloat("adiabaticIndex", adiabaticIndex);

        compute.SetFloat("Pow2Factor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow2DerivativeFactor", 12 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("Pow3Factor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("Pow3DerivativeFactor", 30 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("PolynomialPow6Factor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));

        float[] energies = new float[particleCount];
        InternalEnergyBuffer.GetData(energies);
        for (int i = 0; i < particleCount; i++)
        {
            if (energies[i] >= 300)
            {
                print(energies[i]);
            }
        }
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(NebulaParticleSpawner.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
        InternalEnergyBuffer.SetData(spawnData.energies);
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

    void SampleFrameRate(float deltaTime)
    {
        totalTimeElapsed += deltaTime;
        timeElapsed += deltaTime;

        if (timeElapsed >= TimeBetweenSamples)
        {
            float fps = 1.0f / deltaTime;
            logData.Add($"{totalTimeElapsed:F2},{fps:F2}");
            timeElapsed = 0.0f;
        }
    }

    private void OnApplicationQuit()
    {
        if (!enableDataCollection) return;

        string path = Path.Combine("C:/My_Storage/Python_Stuff/FluidSimDataLogs/TextData", logFileName);
        if (File.Exists(path)) { Debug.Log("Log attempted for file name that already exists"); return; }
        File.WriteAllLines(path, logData);
        Debug.Log($"Logged Frame Data to path: {path}");
    }

    // Clears the memory of all the buffers from the compute shader when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(positionBuffer, velocityBuffer, densityBuffer, predictedPositionBuffer, spatialIndices, spatialOffsets, OctreeBuffer, SpatialHashes, InternalEnergyBuffer);
    }
}
