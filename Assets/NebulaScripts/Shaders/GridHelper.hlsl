// Offsets to loop through (origin is the particles grid position. This loops through a 3x3 area around it)
static const int2 GridOffsets2D[9] =
{
    int2(-1, 1),
    int2(0, 1),
    int2(1, 1),
    int2(-1, 0),
    int2(0, 0),
    int2(1, 0),
    int2(-1, -1),
    int2(0, -1),
    int2(1, -1),
};

static const int3 GridOffsets3D[27] =
{
    int3(-1, -1, -1),
	int3(-1, -1, 0),
	int3(-1, -1, 1),
	int3(-1, 0, -1),
	int3(-1, 0, 0),
	int3(-1, 0, 1),
	int3(-1, 1, -1),
	int3(-1, 1, 0),
	int3(-1, 1, 1),
	int3(0, -1, -1),
	int3(0, -1, 0),
	int3(0, -1, 1),
	int3(0, 0, -1),
	int3(0, 0, 0),
	int3(0, 0, 1),
	int3(0, 1, -1),
	int3(0, 1, 0),
	int3(0, 1, 1),
	int3(1, -1, -1),
	int3(1, -1, 0),
	int3(1, -1, 1),
	int3(1, 0, -1),
	int3(1, 0, 0),
	int3(1, 0, 1),
	int3(1, 1, -1),
	int3(1, 1, 0),
	int3(1, 1, 1)
};

// constant integers (random large prime numbers) that are used for hashing the grid positions
static const uint XKey = 15823;
static const uint YKey = 9737333;
static const uint ZKey = 440817757;

// returns the grid coordinate for the given particle position
int2 GetGridPos2D(float2 Pos, float SmoothingRadius)
{
    return (int2) floor(Pos / SmoothingRadius); // grid size is based on smoothing radius
}

int3 GetGridPos3D(float3 Pos, float SmoothingRadius)
{
    return (int3) floor(Pos / SmoothingRadius);
}

// Converts GridPos to an unsinged int based on the key values above
uint HashGridPos2D(int2 GridPos)
{
    GridPos = (uint2) GridPos;
    uint XHash = GridPos.x * XKey;
    uint YHash = GridPos.y * YKey;
    return (XHash + YHash);
}

uint HashGridPos3D(int3 GridPos)
{
    GridPos = (uint3) GridPos;
    uint XHash = GridPos.x * XKey;
    uint YHash = GridPos.y * YKey;
    uint ZHash = GridPos.z * ZKey;
    return (XHash + YHash + ZHash);
}

uint KeyFromHash(uint hash, uint tableSize)
{
    return hash % tableSize;
}