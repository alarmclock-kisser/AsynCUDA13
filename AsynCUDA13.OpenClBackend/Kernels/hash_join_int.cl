// Probes a GPU hash index to join on int keys; writes the matched build row id (or -1).
// Launched with global size = probeLength (one work-item per probe key).
__kernel void hash_join_int(
	__global const int* probeKeys,
	__global const int* tableKeys,
	__global const int* tableValues,
	__global int* outRowIds,
	int probeLength,
	int tableSize)
{
	int idx = get_global_id(0);
	if (idx >= probeLength)
	{
		return;
	}

	int result = -1;

	if (tableSize > 0)
	{
		int key = probeKeys[idx];
		uint hash = ((uint)key * 2654435761u) % (uint)tableSize;

		for (int probe = 0; probe < tableSize; probe++)
		{
			int slot = (int)((hash + (uint)probe) % (uint)tableSize);
			int candidate = tableKeys[slot];
			if (candidate == -1)
			{
				break;
			}
			if (candidate == key)
			{
				result = tableValues[slot];
				break;
			}
		}
	}

	outRowIds[idx] = result;
}
