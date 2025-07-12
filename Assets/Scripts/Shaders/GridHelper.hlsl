// Offsets to loop through (origin is the particles grid position. This loops through a 3x3 area around it)
static const int2 GridOffsets[9] =
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

// constant integers that are used for hashing the grid positions
static const uint XKey = 19702;
static const uint YKey = 7548546;

// returns the grid coordinate for the given particle position
int2 GetGridPos2D(float2 Pos, float SmoothingRadius)
{
    return (int2) floor(Pos / SmoothingRadius); // grid size is based on smoothing radius
}

// Converts GridPos to an unsinged int based on the key values above
uint HashGridPos2D(int2 GridPos)
{
    GridPos = (uint2) GridPos;
    uint XHash = GridPos.x * XKey;
    uint YHash = GridPos.y * YKey;
    return (XHash + YHash);
}

uint KeyFromHash(uint hash, uint tableSize)
{
    return hash % tableSize;
}