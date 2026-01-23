using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using Unity.VisualScripting;
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
    public ComputeBuffer particleBuffer { get; private set; }
    ComputeBuffer ResultantForceBuffer;
    ComputeBuffer OctreeBuffer;
    ComputeBuffer SpatialHashes;
    ComputeBuffer debugBuffer;

    // spatial hash buffers and sorters
    ComputeBuffer SpatialDataBuffer;
    ComputeBuffer SpatialOffsetsBuffer;
    BufferSorter sorter;

    [Header("Simulation Settings")]
    public bool fixedTimeStep;
    public float timeScale;
    public float damping;
    public float pressureMultiplier;
    public float viscocityMultiplier;
    public float gasConstant;
    public float adiabaticIndex;
    public bool useXSPH;
    public bool isPaused;

    [Header("Smoothing Radius Settings")]
    public float minFactor;
    public float spatialStage1Size;
    public float spatialStage2Size;
    public float spatialStage3Size;
    public bool useDynamicSmoothingRadius;

    [Header("Gravity Settings")]
    public float gravity;
    public float particleMass;
    public float barnesHutAccuracyThreshold;
    public float softeningLength;

    [Header("Entropy Settings")]
    public float InitialTemperature;
    public float BoltzmannConstant;
    public float ProtonMass;
    public float MeanMolecularWeight;
    public float coolingLambda;
    public float coolingAlpha;
    public int coolingSubcycles;
    public float conductionCoefficient;

    [Header("Viscosity Settings")]
    public float viscosityAlpha;
    public float viscosityBeta;
    public float viscosityEpsilon;

    [Header("Fusion Settings")]
    public float temperatureThreshold;
    public float rateCoefficient;
    public float energyPerUnitMass;

    [Header("Data Collection Settings")]
    public bool enableDataCollection;
    public float TimeBetweenSamples;
    public float TotalSampleTime;
    public string logFileName;
    private bool canWriteLog = false;

    // kernel IDs for the compute shader
    const int UpdatePredictionsKernel = 0;
    const int gridHashKernel = 1;
    const int smoothingRadiusKernel = 2;
    const int balsaraFactorKernel = 3;
    const int fusionKernel = 4;
    const int updateEntropyKernel = 5;
    const int gravityKernel = 6;
    const int pressureCorrectionKernel = 7;
    const int pressureForceKernel = 8;
    const int updatePositionKernel = 9;
    const int initialiseEntropyKernel = 10;

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
        particleBuffer = ComputeHelper.CreateStructuredBuffer<ParticleData>(particleCount);
        OctreeBuffer = ComputeHelper.CreateStructuredBuffer<OctreeNode>(octreeManager.NumOfNodes);
        SpatialHashes = ComputeHelper.CreateStructuredBuffer<uint>(octreeManager.NumBottomLayerNodes * (int)octreeManager.NumOfHashesPerLeafNode);
        debugBuffer = ComputeHelper.CreateStructuredBuffer<float2>(particleCount);
        ResultantForceBuffer = ComputeHelper.CreateStructuredBuffer<float3>(particleCount);
        SpatialDataBuffer = ComputeHelper.CreateStructuredBuffer<SpatialData>(particleCount);
        SpatialOffsetsBuffer = ComputeHelper.CreateStructuredBuffer<uint3>(particleCount);
        SetInitialBufferData(spawnData);

        // tell the computer shader which kernels have access to which buffers
        ComputeHelper.SetBuffer(compute, particleBuffer, "ParticleBuffer", UpdatePredictionsKernel, gridHashKernel, pressureForceKernel, gravityKernel, updateEntropyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel, balsaraFactorKernel, initialiseEntropyKernel, fusionKernel);
        ComputeHelper.SetBuffer(compute, OctreeBuffer, "Octree", gravityKernel);
        ComputeHelper.SetBuffer(compute, SpatialHashes, "SpatialHashes", gravityKernel);
        ComputeHelper.SetBuffer(compute, debugBuffer, "DebugBuffer", fusionKernel);
        ComputeHelper.SetBuffer(compute, ResultantForceBuffer, "ResultantForces", updatePositionKernel, pressureForceKernel, gravityKernel, UpdatePredictionsKernel, gravityKernel, updateEntropyKernel);
        ComputeHelper.SetBuffer(compute, SpatialDataBuffer, "SpatialDataBuffer", gridHashKernel, pressureForceKernel, gravityKernel, updateEntropyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel, balsaraFactorKernel);
        ComputeHelper.SetBuffer(compute, SpatialOffsetsBuffer, "SpatialOffsetDataBuffer", gridHashKernel, pressureForceKernel, gravityKernel, updateEntropyKernel, updatePositionKernel, smoothingRadiusKernel, pressureCorrectionKernel, balsaraFactorKernel);
        compute.SetInt("numParticles", particleCount);

        sorter = new();
        sorter.SetBuffers(SpatialDataBuffer, SpatialOffsetsBuffer);

        octreeManager.SetBuffers(OctreeBuffer, SpatialHashes, spatialStage1Size, SpatialDataBuffer, SpatialOffsetsBuffer, particleCount, particleBuffer, particleMass);

        InitialiseParticleProperties();

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
            canWriteLog = true;
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
        // PREDICTED POSITIONS
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel);

        // SPATIAL HASHING AND SORTING
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel); // reset the spatial hash buffers
        sorter.SortAndCalcOffsets(); // sort the indices and calculate the offsets

        // SMOOTHING RADIUS AND DENSITY
        // calculates the smoothing radius and density for each particle
        ComputeHelper.Dispatch(compute, particleCount, smoothingRadiusKernel);

        // BALSARA FACTOR
        ComputeHelper.Dispatch(compute, particleCount, balsaraFactorKernel);

        // FUSION
        ComputeHelper.Dispatch(compute, particleCount, fusionKernel);

        // ENTROPY AND VISCOCITY
        // updates the entropy (viscocity, conduction, cooling) and calculates the viscocity force
        ComputeHelper.Dispatch(compute, particleCount, updateEntropyKernel);

        // GRAVITY AND OCTREE
        octreeManager.UpdateOctree(); // update the octree mass values
        ComputeHelper.Dispatch(compute, particleCount, gravityKernel); // apply gravity using the octree

        // PRESSURE CORRECTIONS
        ComputeHelper.Dispatch(compute, particleCount, pressureCorrectionKernel);

        // PRESSURE FORCE
        ComputeHelper.Dispatch(compute, particleCount, pressureForceKernel);

        // APPLY VELOCITY
        ComputeHelper.Dispatch(compute, particleCount, updatePositionKernel);

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
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetBool("useXSPH", useXSPH);
        compute.SetFloat("viscocityMultiplier", viscocityMultiplier);
        compute.SetFloat("gasConstant", gasConstant);
        compute.SetFloat("adiabaticIndex", adiabaticIndex);
        compute.SetBool("useDynamicSmoothingRadius", useDynamicSmoothingRadius);

        compute.SetFloat("protonMass", ProtonMass);
        compute.SetFloat("meanMolecularWeight", MeanMolecularWeight);
        compute.SetFloat("boltzmannConstant", BoltzmannConstant);
        compute.SetFloat("coolingLambda", coolingLambda);
        compute.SetFloat("coolingAlpha", coolingAlpha);
        compute.SetInt("subcycles", coolingSubcycles);
        compute.SetFloat("viscAlpha", viscosityAlpha);
        compute.SetFloat("viscBeta", viscosityBeta);
        compute.SetFloat("viscEpsilon", viscosityEpsilon);
        compute.SetFloat("conductionCoefficient", conductionCoefficient);

        compute.SetFloat("fusionTempThreshold", temperatureThreshold);
        compute.SetFloat("fusionRateCoefficient", rateCoefficient);
        compute.SetFloat("energyPerUnitMass", energyPerUnitMass);

        compute.SetFloat("stage1Size", spatialStage1Size);
        compute.SetFloat("stage2Size", spatialStage2Size);
        compute.SetFloat("stage3Size", spatialStage3Size);
        compute.SetFloat("minSizeFactor", minFactor);

        compute.SetFloat("Pow2Factor", 6 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 4)));
        compute.SetFloat("Pow2DerivativeFactor", 12 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 4)));
        compute.SetFloat("Pow3Factor", 10 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 5)));
        compute.SetFloat("Pow3DerivativeFactor", 30 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 5)));
        compute.SetFloat("PolynomialPow6Factor", 4 / (Mathf.PI * Mathf.Pow(spatialStage1Size, 8)));

        compute.SetFloat("CubicSplineFactor", 3 / (4*spatialStage1Size));

        ShowDebugData();
    }

    // Sets the initial buffer data for the simulation
    void SetInitialBufferData(NebulaParticleSpawner.ParticleSpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.positions.Length]; // This prevents the modification of the inital spawn data
        System.Array.Copy(spawnData.positions, allPoints, spawnData.positions.Length);

        ParticleData[] allParticles = new ParticleData[particleCount];

        for (int i = 0; i < allParticles.Length; i++)
        {
            ParticleData particle = new ParticleData
            {
                position = spawnData.positions[i],
                predictedPos = spawnData.positions[i],
                velocity = spawnData.velocities[i],
                entropy = 0.0f,
                density = 0.0f,
                smoothingRadius = spatialStage1Size,
                pressureCorrection = 0.0f,
                balsaraFactor = 1.0f,
                temperature = InitialTemperature,
                hydroWeight = 1.0f,
                meanMolecularWeight = 0.0f
            };
            allParticles[i] = particle;
        }

        particleBuffer.SetData(allParticles);
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

    void ShowDebugData()
    {
        float2[] debugData = new float2[particleCount];
        debugBuffer.GetData(debugData);

        float highestRate = 0.0f;
        for (int i = 0; i < debugData.Length; i++)
        {
            highestRate += debugData[i].x;
        }

        highestRate /= debugData.Length;
        Debug.Log("average temp: " + highestRate);
    }

    private void OnApplicationQuit()
    {
        if (!canWriteLog) return;

        string path = Path.Combine("C:/My_Storage/Python_Stuff/FluidSimDataLogs/TextData", logFileName);
        if (File.Exists(path)) { Debug.Log("Log attempted for file name that already exists"); return; }
        File.WriteAllLines(path, logData);
        Debug.Log($"Logged Frame Data to path: {path}");
    }

    // runs the first few steps of the simulation to initialise particle properties like density and entropy
    private void InitialiseParticleProperties()
    {
        ComputeHelper.Dispatch(compute, particleCount, UpdatePredictionsKernel);
        ComputeHelper.Dispatch(compute, particleCount, gridHashKernel);
        sorter.SortAndCalcOffsets();
        ComputeHelper.Dispatch(compute, particleCount, smoothingRadiusKernel);
        ComputeHelper.Dispatch(compute, particleCount, initialiseEntropyKernel); // only run here to initialse entropy based on initial temperature and density
    }

    // Clears the memory of all the buffers from the compute shader when the program is closed
    private void OnDestroy()
    {
        ComputeHelper.Release(particleBuffer, OctreeBuffer, SpatialHashes, debugBuffer, ResultantForceBuffer, SpatialDataBuffer, SpatialOffsetsBuffer);
    }
}

public struct SpatialData
{
    uint index1;
    uint hash1;
    uint key1;

    uint index2;
    uint hash2;
    uint key2;

    uint index3;
    uint hash3;
    uint key3;
}

public struct  ParticleData
{
    public Vector3 position;
    public Vector3 predictedPos;
    public Vector3 velocity;
    public float density;
    public float smoothingRadius;
    public float entropy;
    public float pressureCorrection;
    public float balsaraFactor;
    public float temperature;
    public float hydroWeight;
    public float meanMolecularWeight;
}